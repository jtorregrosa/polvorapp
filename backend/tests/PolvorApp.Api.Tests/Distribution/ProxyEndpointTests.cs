using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.ComparsaOrders.Entries;
using PolvorApp.ComparsaOrders.Orders;
using PolvorApp.Distribution.Persistence;
using PolvorApp.FestivalEditions.Contracts;
using static PolvorApp.Api.Tests.Infrastructure.DistributionData;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;
using static PolvorApp.Api.Tests.Infrastructure.OrderData;

namespace PolvorApp.Api.Tests.Distribution;

/// <summary>
/// Specs "Pickup proxies (UC-19, BR-06)", "Who may manage pickup proxies (BR-10, BR-12)" and
/// "Distribution changes are audited" (design D4, D5, D8): registering and removing proxies.
/// </summary>
public sealed class ProxyEndpointTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private static readonly string[] AbsenceCodes = ["proxies.proxyAbsent", "proxies.holderIsProxy"];

    private OrderTestHost _orders = null!;
    private ComparsaOrder _order = null!;
    private EditionEntry _holder = null!;
    private EditionEntry _reserve = null!;

    public async ValueTask InitializeAsync()
    {
        _orders = await OrderTestHost.StartAsync(postgres, mailpit);

        // Orders closed and Norte's order validated: proxies are still managed (maintainer decision).
        await _orders.SetOrdersOpenAsync(false);
        _order = await _orders.AddOrderAsync(_orders.Current, _orders.Own.Id);
        _holder = await _orders.AddLicensedEntryAsync(_order, "Abad Sintética", ValidThrough, powderKg: 2);
        _reserve = await _orders.AddLicensedEntryAsync(_order, "Zamora Sintético", ValidThrough, status: ArquebusierStatus.Reserve);
    }

    public async ValueTask DisposeAsync() => await _orders.DisposeAsync();

    [Fact]
    public async Task A_FiringChief_registers_a_powder_proxy_and_it_is_audited_without_names()
    {
        using var response = await _orders.FiringChief.RegisterProxyAsync(_orders.Current.Id, _holder.Id, _reserve.Id);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var proxy = await ReadAsync<JsonElement>(response);
        Assert.Equal(("POWDER", _holder.Id, _reserve.Id), (proxy.GetProperty("type").GetString(), proxy.GetProperty("holder").GetProperty("entryId").GetGuid(), proxy.GetProperty("proxy").GetProperty("entryId").GetGuid()));
        var entry = Assert.Single(await _orders.Host.AuditEntriesAsync("PickupProxyAuthorised"));
        Assert.Equal((_orders.Registry.FiringChiefId, (Guid?)_orders.Own.Id, "PickupProxy"), (entry.ActorUserId, entry.ComparsaId, entry.EntityType));
        Assert.Contains(_holder.Id.ToString(), entry.Data!, StringComparison.Ordinal);
        Assert.DoesNotContain("Abad", entry.Data!, StringComparison.Ordinal);
        Assert.DoesNotContain(_reserve.NationalId!, entry.Data!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task One_proxy_collects_for_two_holders()
    {
        var second = await _orders.AddLicensedEntryAsync(_order, "Bernabeu Sintética", ValidThrough, powderKg: 1);

        using var first = await _orders.FiringChief.RegisterProxyAsync(_orders.Current.Id, _holder.Id, _reserve.Id);
        using var other = await _orders.FiringChief.RegisterProxyAsync(_orders.Current.Id, second.Id, _reserve.Id);

        Assert.Equal((HttpStatusCode.Created, HttpStatusCode.Created), (first.StatusCode, other.StatusCode));
    }

    [Fact]
    public async Task An_entry_outside_the_holders_order_is_not_in_the_order_whatever_it_is()
    {
        var otherOrder = await _orders.AddOrderAsync(_orders.Current, _orders.Other.Id);
        var otherComparsa = await _orders.AddLicensedEntryAsync(otherOrder, "Vecina Sintética", ValidThrough);
        var pastOrder = await _orders.AddOrderAsync(_orders.Previous, _orders.Own.Id);
        var pastEntry = await _orders.AddLicensedEntryAsync(pastOrder, "Pasada Sintética", ValidThrough);

        foreach (var proxy in new[] { otherComparsa.Id, pastEntry.Id, Guid.CreateVersion7() })
        {
            using var response = await _orders.Admin.RegisterProxyAsync(_orders.Current.Id, _holder.Id, proxy);
            await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation");
            Assert.Equal("notInOrder", (await ErrorsAsync(response))["proxyEntryId"]);
        }

        Assert.Empty(await _orders.Host.AuditEntriesAsync("PickupProxyAuthorised"));
    }

    [Fact]
    public async Task The_holder_cannot_be_their_own_proxy_and_must_have_something_to_collect()
    {
        var owned = await _orders.AddLicensedEntryAsync(_order, "Propia Sintética", ValidThrough, powderKg: 1, weaponSource: WeaponSource.Owned);

        using var self = await _orders.FiringChief.RegisterProxyAsync(_orders.Current.Id, _holder.Id, _holder.Id);
        using var weapons = await _orders.FiringChief.RegisterProxyAsync(_orders.Current.Id, owned.Id, _reserve.Id, "WEAPONS");
        using var reserve = await _orders.FiringChief.RegisterProxyAsync(_orders.Current.Id, _reserve.Id, _holder.Id);

        Assert.Equal("sameAsHolder", (await ErrorsAsync(self))["proxyEntryId"]);
        Assert.Equal("nothingToCollect", (await ErrorsAsync(weapons))["holderEntryId"]);
        Assert.Equal("nothingToCollect", (await ErrorsAsync(reserve))["holderEntryId"]);
    }

    [Fact]
    public async Task A_proxy_without_an_active_license_on_the_day_is_refused()
    {
        using (var day = await DistributionRequests.PlanAsync(_orders.Admin, _orders.Current.Id, "POWDER", "2031-04-18", "Paraje Sintético"))
        {
            Assert.Equal(HttpStatusCode.Created, day.StatusCode);
        }

        var expiring = await _orders.AddLicensedEntryAsync(_order, "Caducada Sintética", new DateOnly(2031, 3, 31), status: ArquebusierStatus.Reserve);
        var pending = await _orders.AddLicensedEntryAsync(_order, "Pendiente Sintética", null, status: ArquebusierStatus.Reserve, pending: true);
        var none = await _orders.AddLicensedEntryAsync(_order, "Sinlicencia Sintética", null, status: ArquebusierStatus.Reserve);

        foreach (var proxy in new[] { expiring.Id, pending.Id, none.Id })
        {
            using var response = await _orders.FiringChief.RegisterProxyAsync(_orders.Current.Id, _holder.Id, proxy);
            Assert.Equal("licenseInvalid", (await ErrorsAsync(response))["proxyEntryId"]);
        }
    }

    [Fact]
    public async Task An_erased_holder_or_proxy_is_refused_and_nothing_is_stored()
    {
        var erased = await _orders.AddLicensedEntryAsync(_order, "Borrada Sintética", ValidThrough, powderKg: 1);
        await _orders.EraseEntryAsync(erased.Id);

        using var asProxy = await _orders.FiringChief.RegisterProxyAsync(_orders.Current.Id, _holder.Id, erased.Id);
        using var asHolder = await _orders.FiringChief.RegisterProxyAsync(_orders.Current.Id, erased.Id, _reserve.Id);

        Assert.Equal("entryErased", (await ErrorsAsync(asProxy))["proxyEntryId"]);
        Assert.Equal("entryErased", (await ErrorsAsync(asHolder))["holderEntryId"]);
        Assert.Empty(await _orders.Host.AuditEntriesAsync("PickupProxyAuthorised"));
    }

    [Fact]
    public async Task The_write_lock_tells_whether_an_entry_was_erased()
    {
        await using var scope = _orders.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DistributionDbContext>();
        var entries = scope.ServiceProvider.GetRequiredService<IEditionEntries>();
        await using var transaction = await db.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
        var dbTransaction = transaction.GetDbTransaction();

        var before = await entries.AnyErasedForWriteAsync([_holder.Id, _reserve.Id], dbTransaction, TestContext.Current.CancellationToken);
        await transaction.RollbackAsync(TestContext.Current.CancellationToken);
        await _orders.EraseEntryAsync(_reserve.Id);
        await using var again = await db.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
        var after = await entries.AnyErasedForWriteAsync([_holder.Id, _reserve.Id], again.GetDbTransaction(), TestContext.Current.CancellationToken);

        Assert.False(before);
        Assert.True(after);
    }

    [Fact]
    public async Task A_second_proxy_for_the_same_holder_and_type_is_refused()
    {
        var other = await _orders.AddLicensedEntryAsync(_order, "Otra Sintética", ValidThrough, status: ArquebusierStatus.Reserve);
        using var first = await _orders.FiringChief.RegisterProxyAsync(_orders.Current.Id, _holder.Id, _reserve.Id);

        using var second = await _orders.FiringChief.RegisterProxyAsync(_orders.Current.Id, _holder.Id, other.Id);

        await AssertProblemAsync(second, HttpStatusCode.Conflict, "proxies.alreadyAuthorised");
    }

    [Fact]
    public async Task Someone_absent_cannot_collect_and_a_proxy_cannot_be_absent()
    {
        var c = await _orders.AddLicensedEntryAsync(_order, "Climent Sintético", ValidThrough, powderKg: 1);
        using var aToB = await _orders.FiringChief.RegisterProxyAsync(_orders.Current.Id, _holder.Id, _reserve.Id);
        Assert.Equal(HttpStatusCode.Created, aToB.StatusCode);

        // A is absent (B collects for them): A cannot collect for C.
        using var absentProxy = await _orders.FiringChief.RegisterProxyAsync(_orders.Current.Id, c.Id, _holder.Id);
        await AssertProblemAsync(absentProxy, HttpStatusCode.Conflict, "proxies.proxyAbsent");

        // B2 collects for C: B2 cannot be absent themselves.
        var b = await _orders.AddLicensedEntryAsync(_order, "Bernabeu Sintética", ValidThrough, powderKg: 1);
        using var bCollects = await _orders.FiringChief.RegisterProxyAsync(_orders.Current.Id, c.Id, b.Id);
        using var bAbsent = await _orders.FiringChief.RegisterProxyAsync(_orders.Current.Id, b.Id, _reserve.Id);
        Assert.Equal(HttpStatusCode.Created, bCollects.StatusCode);
        await AssertProblemAsync(bAbsent, HttpStatusCode.Conflict, "proxies.holderIsProxy");
    }

    [Fact]
    public async Task Two_proxies_for_the_same_holder_at_once_leave_one()
    {
        var other = await _orders.AddLicensedEntryAsync(_order, "Otra Sintética", ValidThrough, status: ArquebusierStatus.Reserve);

        var responses = await Task.WhenAll(
            _orders.FiringChief.RegisterProxyAsync(_orders.Current.Id, _holder.Id, _reserve.Id),
            _orders.Admin.RegisterProxyAsync(_orders.Current.Id, _holder.Id, other.Id));

        Assert.Equal([HttpStatusCode.Created, HttpStatusCode.Conflict], responses.Select(r => r.StatusCode).Order());
        Assert.Equal(1, await DistributionRequests.CountProxiesAsync(_orders.Services));
        DisposeAll(responses);
    }

    [Fact]
    public async Task Two_holders_naming_each_other_at_once_leave_one_proxy()
    {
        // Both active with powder and a valid license: only the absence rules can refuse one of them.
        var b = await _orders.AddLicensedEntryAsync(_order, "Bernabeu Sintética", ValidThrough, powderKg: 1);
        var c = await _orders.AddLicensedEntryAsync(_order, "Climent Sintético", ValidThrough, powderKg: 1);

        var responses = await Task.WhenAll(
            _orders.FiringChief.RegisterProxyAsync(_orders.Current.Id, c.Id, b.Id),
            _orders.Admin.RegisterProxyAsync(_orders.Current.Id, b.Id, c.Id));

        Assert.Equal([HttpStatusCode.Created, HttpStatusCode.Conflict], responses.Select(r => r.StatusCode).Order());
        var refused = Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Contains(await ProblemCodeAsync(refused), AbsenceCodes);
        Assert.Equal(1, await DistributionRequests.CountProxiesAsync(_orders.Services));
        DisposeAll(responses);
    }

    [Fact]
    public async Task A_comparsa_whose_proxies_are_locked_past_the_timeout_is_busy_and_nothing_is_stored()
    {
        await using var holder = _orders.Services.CreateAsyncScope();
        var db = holder.ServiceProvider.GetRequiredService<DistributionDbContext>();
        await using (var transaction = await db.Database.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            await db.LockProxiesAsync(_orders.Current.Id, _orders.Own.Id, TestContext.Current.CancellationToken);

            // The comparsa stays locked for longer than the 5 s lock timeout of the registration.
            using var response = await _orders.FiringChief.RegisterProxyAsync(_orders.Current.Id, _holder.Id, _reserve.Id);
            await transaction.RollbackAsync(TestContext.Current.CancellationToken);

            await AssertProblemAsync(response, HttpStatusCode.ServiceUnavailable, "distribution.busy");
        }

        Assert.Equal(0, await DistributionRequests.CountProxiesAsync(_orders.Services));
        Assert.Empty(await _orders.Host.AuditEntriesAsync("PickupProxyAuthorised"));
    }

    [Theory]
    [InlineData(null, true, true, "type", "required")]
    [InlineData("FLASKS", true, true, "type", "invalid")]
    [InlineData("POWDER", false, true, "holderEntryId", "required")]
    [InlineData("POWDER", true, false, "proxyEntryId", "required")]
    public async Task Missing_or_unknown_fields_are_named(string? type, bool withHolder, bool withProxy, string field, string reason)
    {
        using var response = await _orders.FiringChief.PostAsJsonAsync(
            $"/api/distribution/editions/{_orders.Current.Id}/proxies",
            new { holderEntryId = withHolder ? _holder.Id : (Guid?)null, proxyEntryId = withProxy ? _reserve.Id : (Guid?)null, type },
            TestContext.Current.CancellationToken);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation");
        Assert.Equal(reason, (await ErrorsAsync(response))[field]);
    }

    [Fact]
    public async Task A_FiringChief_learns_nothing_about_entries_outside_their_order()
    {
        var otherOrder = await _orders.AddOrderAsync(_orders.Current, _orders.Other.Id);
        var foreign = await _orders.AddLicensedEntryAsync(otherOrder, "Vecina Sintética", ValidThrough);
        var answers = new List<(string?, Dictionary<string, string>)>();

        foreach (var proxy in new[] { foreign.Id, Guid.CreateVersion7() })
        {
            using var response = await _orders.FiringChief.RegisterProxyAsync(_orders.Current.Id, _holder.Id, proxy);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            answers.Add((await ProblemCodeAsync(response), await ErrorsAsync(response)));
        }

        // Same code and same field errors: only the trace id differs.
        Assert.Equal(answers[0].Item1, answers[1].Item1);
        Assert.Equal(answers[0].Item2, answers[1].Item2);
    }

    private static void DisposeAll(IEnumerable<HttpResponseMessage> responses)
    {
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task Another_comparsas_holder_or_a_draft_edition_is_not_found_for_a_FiringChief()
    {
        var otherOrder = await _orders.AddOrderAsync(_orders.Current, _orders.Other.Id);
        var otherHolder = await _orders.AddLicensedEntryAsync(otherOrder, "Vecina Sintética", ValidThrough, powderKg: 2);
        var otherProxy = await _orders.AddLicensedEntryAsync(otherOrder, "Vecino Sintético", ValidThrough, status: ArquebusierStatus.Reserve);
        var draft = NewEdition(2032, EditionStatus.Draft);
        await _orders.Services.SaveEditionsAsync(draft);

        using var outOfScope = await _orders.FiringChief.RegisterProxyAsync(_orders.Current.Id, otherHolder.Id, otherProxy.Id);
        using var wrongEdition = await _orders.FiringChief.RegisterProxyAsync(draft.Id, _holder.Id, _reserve.Id);
        using var asAdmin = await _orders.Admin.RegisterProxyAsync(_orders.Current.Id, otherHolder.Id, otherProxy.Id);

        await AssertProblemAsync(outOfScope, HttpStatusCode.NotFound, "distribution.notFound");
        await AssertProblemAsync(wrongEdition, HttpStatusCode.NotFound, "distribution.notFound");
        Assert.Equal(HttpStatusCode.Created, asAdmin.StatusCode);
    }

    [Fact]
    public async Task In_a_closed_edition_a_FiringChief_cannot_change_proxies_but_an_Admin_can()
    {
        var proxyId = await RegisterAsync();
        await _orders.SetCurrentStatusAsync(EditionStatus.Closed);

        using var register = await _orders.FiringChief.RegisterProxyAsync(_orders.Current.Id, _holder.Id, _reserve.Id, "WEAPONS");
        using var remove = await _orders.FiringChief.RemoveProxyAsync(proxyId);
        await AssertProblemAsync(register, HttpStatusCode.Conflict, "distribution.editionNotInProgress");
        await AssertProblemAsync(remove, HttpStatusCode.Conflict, "distribution.editionNotInProgress");

        using var adminRemove = await _orders.Admin.RemoveProxyAsync(proxyId);
        Assert.Equal(HttpStatusCode.NoContent, adminRemove.StatusCode);
        var entry = Assert.Single(await _orders.Host.AuditEntriesAsync("PickupProxyRemoved"));
        Assert.Equal((_orders.Registry.AdminId, (Guid?)_orders.Own.Id), (entry.ActorUserId, entry.ComparsaId));
    }

    [Fact]
    public async Task A_FiringChief_cannot_remove_another_comparsas_proxy_and_unknown_proxies_are_not_found()
    {
        var otherOrder = await _orders.AddOrderAsync(_orders.Current, _orders.Other.Id);
        var otherHolder = await _orders.AddLicensedEntryAsync(otherOrder, "Vecina Sintética", ValidThrough, powderKg: 2);
        var otherProxy = await _orders.AddLicensedEntryAsync(otherOrder, "Vecino Sintético", ValidThrough, status: ArquebusierStatus.Reserve);
        using var created = await _orders.Admin.RegisterProxyAsync(_orders.Current.Id, otherHolder.Id, otherProxy.Id);
        var proxyId = (await ReadAsync<JsonElement>(created)).GetProperty("id").GetGuid();

        using var outOfScope = await _orders.FiringChief.RemoveProxyAsync(proxyId);
        using var unknown = await _orders.Admin.RemoveProxyAsync(Guid.CreateVersion7());

        await AssertProblemAsync(outOfScope, HttpStatusCode.NotFound, "proxies.notFound");
        await AssertProblemAsync(unknown, HttpStatusCode.NotFound, "proxies.notFound");
        Assert.Empty(await _orders.Host.AuditEntriesAsync("PickupProxyRemoved"));
    }

    private async Task<Guid> RegisterAsync()
    {
        using var response = await _orders.FiringChief.RegisterProxyAsync(_orders.Current.Id, _holder.Id, _reserve.Id);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await ReadAsync<JsonElement>(response)).GetProperty("id").GetGuid();
    }
}
