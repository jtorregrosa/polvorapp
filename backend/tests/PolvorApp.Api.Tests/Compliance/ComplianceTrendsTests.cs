using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.ArquebusierRegistry.Arquebusiers;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.AuditPrivacy.Persistence;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.FederationCatalog.Contracts;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;
using static PolvorApp.Api.Tests.Infrastructure.OrderData;

namespace PolvorApp.Api.Tests.Compliance;

/// <summary>
/// <c>GET /compliance/trends</c> (spec: Edition trends (UC-07); add-statistics-trends, design D2, D3):
/// counts per started edition in the caller's scope, the gender of the registry today with
/// <c>UNKNOWN</c> for arquebusiers no longer in it, per-comparsa figures only for several comparsas,
/// no personal data and no audit. The FiringChief of the host sees Own and Inactive; Other is outside.
/// </summary>
public sealed class ComplianceTrendsTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private OrderTestHost _orders = null!;
    private Arquebusier _woman = null!;
    private Arquebusier _man = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _orders = await OrderTestHost.StartAsync(postgres, mailpit);
        _woman = RegistryData.NewArquebusier(_orders.Own.Id, "Tendencia Sintética");
        _woman.Gender = Gender.Female;
        _man = RegistryData.NewArquebusier(_orders.Own.Id, "Tendencia Sintético");
        _man.Gender = Gender.Male;
        await _orders.Services.SaveRegistryAsync(_woman, _man);
    }

    public async ValueTask DisposeAsync() => await _orders.DisposeAsync();

    [Fact]
    public async Task Gender_comes_from_the_registry_today_and_an_arquebusier_no_longer_in_it_is_unknown()
    {
        var order = NewOrder(_orders.Current, _orders.Own.Id);
        await _orders.Services.SaveOrdersAsync(
            // A deleted arquebusier leaves the entry without its id (the foreign key is set to null).
            order, NewEntry(order, _woman.Id), NewEntry(order, _man.Id), NewEntry(order, null), NewEntry(order, null));

        var row = Row(await TrendsAsync(_orders.Admin, null), 2031);

        var gender = row.GetProperty("gender");
        Assert.Equal((1, 1, 0, 2), (Int(gender, "female"), Int(gender, "male"), Int(gender, "unspecified"), Int(gender, "unknown")));
        Assert.Equal(4, Int(row, "active"));
        Assert.True(row.GetProperty("provisional").GetBoolean());
    }

    [Fact]
    public async Task An_arquebusier_who_moved_comparsa_keeps_their_gender_and_unspecified_is_counted()
    {
        var moved = RegistryData.NewArquebusier(_orders.Other.Id, "Tendencia Trasladada");
        moved.Gender = Gender.Female;
        var unspecified = RegistryData.NewArquebusier(_orders.Own.Id, "Tendencia Sin Definir");
        unspecified.Gender = Gender.Unspecified;
        await _orders.Services.SaveRegistryAsync(moved, unspecified);
        // Last year they paraded with the FiringChief's comparsa; today they belong to another one.
        var order = NewOrder(_orders.Previous, _orders.Own.Id, OrderStatus.Validated);
        await _orders.Services.SaveOrdersAsync(order, NewEntry(order, moved.Id), NewEntry(order, unspecified.Id));

        var gender = Row(await TrendsAsync(_orders.FiringChief, null), 2030).GetProperty("gender");

        Assert.Equal((1, 0, 1, 0), (Int(gender, "female"), Int(gender, "male"), Int(gender, "unspecified"), Int(gender, "unknown")));
    }

    [Fact]
    public async Task A_comparsa_without_active_entries_in_an_edition_counts_zero_there()
    {
        var previous = NewOrder(_orders.Previous, _orders.Own.Id, OrderStatus.Validated);
        var current = NewOrder(_orders.Current, _orders.Other.Id);
        await _orders.Services.SaveOrdersAsync(previous, NewEntry(previous, _woman.Id), current, NewEntry(current, await PersonAsync(_orders.Other.Id)));

        var trends = await TrendsAsync(_orders.Admin, null);

        var current2031 = Row(trends, 2031).GetProperty("comparsas").EnumerateArray()
            .ToDictionary(c => c.GetProperty("comparsaId").GetGuid(), c => Int(c, "active"));
        Assert.Equal(new Dictionary<Guid, int> { [_orders.Own.Id] = 0, [_orders.Other.Id] = 1 }, current2031);
        Assert.Equal(2, trends.GetProperty("comparsas").GetArrayLength());
    }

    [Fact]
    public async Task Too_many_trend_reads_are_refused()
    {
        await using var limited = await OrderTestHost.StartAsync(
            postgres, mailpit, settings: new Dictionary<string, string?> { ["RateLimits:InsightsReads:PermitLimit"] = "2" });

        for (var i = 0; i < 2; i++)
        {
            using var allowed = await limited.Admin.GetAsync("/api/compliance/trends", Token);
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        using var refused = await limited.Admin.GetAsync("/api/compliance/trends", Token);
        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
    }

    [Fact]
    public async Task Rentals_are_counted_by_weapon_kind()
    {
        var order = NewOrder(_orders.Current, _orders.Own.Id);
        var rented = NewEntry(order, _woman.Id);
        (rented.WeaponSource, rented.RentalWeaponModelId) = (WeaponSource.Rental, _orders.Offered.Id);
        await _orders.Services.SaveOrdersAsync(order, rented);

        var row = Row(await TrendsAsync(_orders.Admin, null), 2031);

        var kinds = row.GetProperty("rentalsByKind").EnumerateArray().Select(k => (k.GetProperty("kind").GetString(), Int(k, "count")));
        Assert.Equal([("TRABUCO", 0), ("ARCABUZ", 1), ("PISTOL", 0)], kinds);
        Assert.Equal(1, Int(row.GetProperty("weaponSources"), "rental"));
    }

    [Fact]
    public async Task An_Admin_gets_per_comparsa_figures_with_the_comparsa_names()
    {
        var own = NewOrder(_orders.Current, _orders.Own.Id);
        var other = NewOrder(_orders.Current, _orders.Other.Id);
        await _orders.Services.SaveOrdersAsync(own, NewEntry(own, _woman.Id), other, NewEntry(other, await PersonAsync(_orders.Other.Id)), NewEntry(other, await PersonAsync(_orders.Other.Id)));

        var trends = await TrendsAsync(_orders.Admin, null);

        var perComparsa = Row(trends, 2031).GetProperty("comparsas").EnumerateArray()
            .ToDictionary(c => c.GetProperty("comparsaId").GetGuid(), c => Int(c, "active"));
        Assert.Equal(1, perComparsa[_orders.Own.Id]);
        Assert.Equal(2, perComparsa[_orders.Other.Id]);
        Assert.Contains(trends.GetProperty("comparsas").EnumerateArray(), c => c.GetProperty("name").GetString() == _orders.Other.Name);
    }

    [Fact]
    public async Task A_FiringChief_with_two_comparsas_counts_only_theirs_and_a_filter_drops_the_per_comparsa_figures()
    {
        var own = NewOrder(_orders.Current, _orders.Own.Id);
        var other = NewOrder(_orders.Current, _orders.Other.Id);
        await _orders.Services.SaveOrdersAsync(own, NewEntry(own, _woman.Id), other, NewEntry(other, await PersonAsync(_orders.Other.Id)));

        var all = await TrendsAsync(_orders.FiringChief, null);
        var filtered = await TrendsAsync(_orders.FiringChief, _orders.Own.Id);

        // The FiringChief is assigned to two comparsas (one inactive, without entries): per-comparsa
        // figures name only comparsas in their scope, never the other one.
        Assert.Equal(1, Int(Row(all, 2031), "active"));
        var perComparsa = Row(all, 2031).GetProperty("comparsas").EnumerateArray()
            .ToDictionary(c => c.GetProperty("comparsaId").GetGuid(), c => Int(c, "active"));
        Assert.Equal(new Dictionary<Guid, int> { [_orders.Own.Id] = 1 }, perComparsa);
        Assert.Equal([_orders.Own.Id], all.GetProperty("comparsas").EnumerateArray().Select(c => c.GetProperty("id").GetGuid()));
        Assert.Equal(1, Int(Row(filtered, 2031), "active"));
        Assert.Empty(Row(filtered, 2031).GetProperty("comparsas").EnumerateArray());
        Assert.Empty(filtered.GetProperty("comparsas").EnumerateArray());
    }

    [Fact]
    public async Task A_comparsa_outside_the_scope_or_unknown_is_not_found()
    {
        using var outside = await _orders.FiringChief.GetAsync($"/api/compliance/trends?comparsaId={_orders.Other.Id}", Token);
        using var unknown = await _orders.Admin.GetAsync($"/api/compliance/trends?comparsaId={Guid.CreateVersion7()}", Token);

        await AssertProblemAsync(outside, HttpStatusCode.NotFound, "compliance.comparsaNotFound");
        await AssertProblemAsync(unknown, HttpStatusCode.NotFound, "compliance.comparsaNotFound");
    }

    [Fact]
    public async Task The_response_holds_counts_only_and_reading_it_is_not_audited()
    {
        var order = NewOrder(_orders.Current, _orders.Own.Id);
        await _orders.Services.SaveOrdersAsync(order, NewEntry(order, _woman.Id));
        var before = await AuditCountAsync();

        using var response = await _orders.Admin.GetAsync("/api/compliance/trends", Token);
        var json = await response.Content.ReadAsStringAsync(Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        foreach (var personal in new[] { _woman.Id.ToString(), _woman.LastName, _woman.NationalId!, "Arcabucero" })
        {
            Assert.DoesNotContain(personal, json, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Equal(before, await AuditCountAsync());
    }

    [Fact]
    public async Task An_anonymous_caller_is_refused()
    {
        using var anonymous = await _orders.Host.NewClientAsync();

        using var response = await anonymous.GetAsync("/api/compliance/trends", Token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<Guid> PersonAsync(Guid comparsaId) => (await _orders.AddArquebusierAsync(comparsaId, "Tendencia Sintética")).Arquebusier.Id;

    private async Task<int> AuditCountAsync()
    {
        await using var scope = _orders.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AuditDbContext>().Set<global::PolvorApp.SharedKernel.Auditing.AuditEntry>().CountAsync(Token);
    }

    private static async Task<JsonElement> TrendsAsync(HttpClient client, Guid? comparsaId)
    {
        using var response = await client.GetAsync(comparsaId is { } id ? $"/api/compliance/trends?comparsaId={id}" : "/api/compliance/trends", Token);
        return await ReadAsync<JsonElement>(response);
    }

    private static JsonElement Row(JsonElement trends, int year) =>
        trends.GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("year").GetInt32() == year);

    private static int Int(JsonElement element, string property) => element.GetProperty(property).GetInt32();
}
