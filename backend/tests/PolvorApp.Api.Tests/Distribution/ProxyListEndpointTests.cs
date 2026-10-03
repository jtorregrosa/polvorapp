using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ArquebusierRegistry.Persistence;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.ComparsaOrders.Entries;
using PolvorApp.ComparsaOrders.Orders;
using PolvorApp.ComparsaOrders.Persistence;
using static PolvorApp.Api.Tests.Infrastructure.DistributionData;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Distribution;

/// <summary>
/// Specs "Distribution visibility (BR-12)", "Proxies that no longer hold" and "Distribution screens" (design
/// D5, D7): the proxies listed with their problems, and the candidates of the add-proxy panel.
/// </summary>
public sealed class ProxyListEndpointTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private OrderTestHost _orders = null!;
    private ComparsaOrder _order = null!;
    private EditionEntry _holder = null!;
    private EditionEntry _reserve = null!;

    public async ValueTask InitializeAsync()
    {
        _orders = await OrderTestHost.StartAsync(postgres, mailpit);
        _order = await _orders.AddOrderAsync(_orders.Current, _orders.Own.Id);
        _holder = await _orders.AddLicensedEntryAsync(_order, "Abad Sintética", ValidThrough, powderKg: 2, weaponSource: WeaponSource.Rental);
        _reserve = await _orders.AddLicensedEntryAsync(_order, "Zamora Sintético", ValidThrough, status: ArquebusierStatus.Reserve);
    }

    public async ValueTask DisposeAsync() => await _orders.DisposeAsync();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Proxies_are_listed_with_names_and_scoped_to_the_FiringChiefs_comparsas()
    {
        await RegisterAsync(_orders.FiringChief, _holder.Id, _reserve.Id);
        var otherOrder = await _orders.AddOrderAsync(_orders.Current, _orders.Other.Id);
        var otherHolder = await _orders.AddLicensedEntryAsync(otherOrder, "Vecina Sintética", ValidThrough, powderKg: 1);
        var otherProxy = await _orders.AddLicensedEntryAsync(otherOrder, "Vecino Sintético", ValidThrough, status: ArquebusierStatus.Reserve);
        await RegisterAsync(_orders.Admin, otherHolder.Id, otherProxy.Id);

        var asFiringChief = await ListAsync(_orders.FiringChief);
        var asAdmin = await ListAsync(_orders.Admin);
        var filtered = await ListAsync(_orders.Admin, _orders.Other.Id);
        using var outOfScope = await _orders.FiringChief.GetAsync($"/api/distribution/editions/{_orders.Current.Id}/proxies?comparsaId={_orders.Other.Id}", Token);

        var own = Assert.Single(asFiringChief);
        Assert.Equal(("Abad Sintética, Arcabucero", "Zamora Sintético, Arcabucero"), (own.GetProperty("holder").GetProperty("name").GetString(), own.GetProperty("proxy").GetProperty("name").GetString()));
        Assert.Equal(JsonValueKind.Null, own.GetProperty("problem").ValueKind);
        Assert.Equal(2, asAdmin.Count);
        Assert.Equal(otherHolder.Id, Assert.Single(filtered).GetProperty("holder").GetProperty("entryId").GetGuid());
        await AssertProblemAsync(outOfScope, HttpStatusCode.NotFound, "distribution.notFound");
    }

    [Fact]
    public async Task A_proxy_whose_holder_has_nothing_left_to_collect_does_not_apply()
    {
        await RegisterAsync(_orders.FiringChief, _holder.Id, _reserve.Id);
        await UpdateEntryAsync(_holder.Id, e => e.PowderKg = 0);

        var proxy = Assert.Single(await ListAsync(_orders.FiringChief));

        Assert.Equal("NOT_APPLICABLE", proxy.GetProperty("problem").GetString());
    }

    [Fact]
    public async Task A_proxy_whose_license_expires_before_the_day_planned_later_is_flagged()
    {
        var expiring = await _orders.AddLicensedEntryAsync(_order, "Breve Sintética", new DateOnly(2031, 4, 10), status: ArquebusierStatus.Reserve);
        using (var early = await _orders.Admin.RegisterProxyAsync(_orders.Current.Id, _holder.Id, expiring.Id, "WEAPONS"))
        {
            // Not planned yet: the reference is the festival's first day (22 April), after the expiry.
            Assert.Equal("licenseInvalid", (await ErrorsAsync(early))["proxyEntryId"]);
        }

        using (var day = await DistributionRequests.PlanAsync(_orders.Admin, _orders.Current.Id, "WEAPONS", "2031-04-05", "Almacén Sintético"))
        {
            Assert.Equal(HttpStatusCode.Created, day.StatusCode);
        }

        await RegisterAsync(_orders.Admin, _holder.Id, expiring.Id, "WEAPONS");
        var planned = Assert.Single((await DistributionRequests.PlanOfAsync(_orders.Admin, _orders.Current.Id)).GetProperty("days").EnumerateArray());
        using (var moved = await DistributionRequests.EditAsync(_orders.Admin, planned.GetProperty("id").GetGuid(), "2031-04-18", "Almacén Sintético", planned.GetProperty("version").GetUInt32()))
        {
            Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        }

        Assert.Equal("LICENSE_INVALID", Assert.Single(await ListAsync(_orders.Admin)).GetProperty("problem").GetString());
    }

    [Fact]
    public async Task An_entry_no_longer_in_the_registry_is_named_from_its_copy_and_its_license_no_longer_holds()
    {
        await RegisterAsync(_orders.FiringChief, _holder.Id, _reserve.Id);
        await using (var scope = _orders.Services.CreateAsyncScope())
        {
            // The orders are closed when an arquebusier is deleted: their entry stays with its copy (BR-14).
            await scope.ServiceProvider.GetRequiredService<ComparsaOrdersDbContext>().Entries
                .Where(e => e.Id == _reserve.Id).ExecuteUpdateAsync(e => e.SetProperty(x => x.ArquebusierId, (Guid?)null), Token);
        }

        var proxy = Assert.Single(await ListAsync(_orders.FiringChief));

        Assert.Equal("Zamora Sintético, Arcabucero", proxy.GetProperty("proxy").GetProperty("name").GetString());
        Assert.Equal("LICENSE_INVALID", proxy.GetProperty("problem").GetString());
    }

    [Fact]
    public async Task Candidates_say_what_each_entry_may_hold_and_why_it_cannot_collect()
    {
        var expired = await _orders.AddLicensedEntryAsync(_order, "Caducada Sintética", new DateOnly(2030, 1, 1), status: ArquebusierStatus.Reserve);
        await RegisterAsync(_orders.FiringChief, _holder.Id, _reserve.Id);

        var candidates = (await CandidatesAsync(_orders.FiringChief, _orders.Own.Id)).ToDictionary(c => c.GetProperty("entryId").GetGuid());

        Assert.Equal(["WEAPONS"], Types(candidates[_holder.Id], "canBeHeldFor"));
        Assert.Contains(("POWDER", "proxyAbsent"), Restrictions(candidates[_holder.Id]));
        Assert.Empty(Types(candidates[_reserve.Id], "canBeHeldFor"));
        Assert.Empty(Restrictions(candidates[_reserve.Id]));
        Assert.Equal([("POWDER", "licenseInvalid"), ("WEAPONS", "licenseInvalid")], Restrictions(candidates[expired.Id]).Order());
        Assert.False(candidates[expired.Id].GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task An_entry_collecting_for_another_cannot_get_a_proxy_of_that_type_only()
    {
        var collector = await _orders.AddLicensedEntryAsync(_order, "Climent Sintético", ValidThrough, powderKg: 1, weaponSource: WeaponSource.Rental);
        await RegisterAsync(_orders.FiringChief, _holder.Id, collector.Id);

        var candidate = (await CandidatesAsync(_orders.FiringChief, _orders.Own.Id)).Single(c => c.GetProperty("entryId").GetGuid() == collector.Id);

        Assert.Equal(["WEAPONS"], Types(candidate, "canBeHeldFor"));
    }

    [Fact]
    public async Task The_candidates_license_follows_each_days_date()
    {
        var shortLicense = await _orders.AddLicensedEntryAsync(_order, "Breve Sintética", new DateOnly(2031, 4, 10), status: ArquebusierStatus.Reserve);
        using (var day = await DistributionRequests.PlanAsync(_orders.Admin, _orders.Current.Id, "WEAPONS", "2031-04-05", "Almacén Sintético"))
        {
            Assert.Equal(HttpStatusCode.Created, day.StatusCode);
        }

        var candidate = (await CandidatesAsync(_orders.FiringChief, _orders.Own.Id)).Single(c => c.GetProperty("entryId").GetGuid() == shortLicense.Id);

        // Valid on the weapons day (5 April), expired by the festival's first day (22 April) used for powder.
        Assert.Equal([("POWDER", "licenseInvalid")], Restrictions(candidate));
    }

    [Fact]
    public async Task Another_comparsas_candidates_are_not_found_for_a_FiringChief()
    {
        using var response = await _orders.FiringChief.GetAsync($"/api/distribution/editions/{_orders.Current.Id}/comparsas/{_orders.Other.Id}/proxy-candidates", Token);

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "distribution.notFound");
        Assert.Empty(await CandidatesAsync(_orders.Admin, _orders.Other.Id));
    }

    private static List<string> Types(JsonElement candidate, string property) =>
        [.. candidate.GetProperty(property).EnumerateArray().Select(t => t.GetString()!)];

    private static List<(string, string)> Restrictions(JsonElement candidate) =>
        [.. candidate.GetProperty("cannotCollect").EnumerateArray().Select(r => (r.GetProperty("type").GetString()!, r.GetProperty("reason").GetString()!))];

    private async Task RegisterAsync(HttpClient client, Guid holder, Guid proxy, string type = "POWDER")
    {
        using var response = await client.RegisterProxyAsync(_orders.Current.Id, holder, proxy, type);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private async Task<List<JsonElement>> ListAsync(HttpClient client, Guid? comparsaId = null)
    {
        var query = comparsaId is { } id ? $"?comparsaId={id}" : string.Empty;
        using var response = await client.GetAsync($"/api/distribution/editions/{_orders.Current.Id}/proxies{query}", Token);
        return [.. (await ReadAsync<JsonElement>(response)).EnumerateArray()];
    }

    private async Task<List<JsonElement>> CandidatesAsync(HttpClient client, Guid comparsaId)
    {
        using var response = await client.GetAsync($"/api/distribution/editions/{_orders.Current.Id}/comparsas/{comparsaId}/proxy-candidates", Token);
        return [.. (await ReadAsync<JsonElement>(response)).EnumerateArray()];
    }

    private async Task UpdateEntryAsync(Guid entryId, Action<EditionEntry> change)
    {
        await using var scope = _orders.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ComparsaOrdersDbContext>();
        var entry = await db.Entries.SingleAsync(e => e.Id == entryId, Token);
        change(entry);
        await db.SaveChangesAsync(Token);
    }
}
