using System.Diagnostics;
using System.Net;
using System.Text.Json;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.ComparsaOrders.Entries;
using PolvorApp.FestivalEditions.Contracts;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;
using static PolvorApp.Api.Tests.Infrastructure.OrderData;

namespace PolvorApp.Api.Tests.Orders;

/// <summary>Spec "Order totals and dashboard (UC-16)": the orders overview and the totals of an order (design D10).</summary>
public sealed class OrderOverviewTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    /// <summary>NFR-05: pages under 2 s; the overview is one request of the page.</summary>
    private static readonly TimeSpan PageBudget = TimeSpan.FromSeconds(2);

    private OrderTestHost _orders = null!;

    public async ValueTask InitializeAsync() => _orders = await OrderTestHost.StartAsync(postgres, mailpit);

    public async ValueTask DisposeAsync() => await _orders.DisposeAsync();

    [Fact]
    public async Task An_Admin_sees_every_active_comparsa_the_status_counts_and_the_edition_totals()
    {
        var validated = NewOrder(_orders.Current, _orders.Own.Id, OrderStatus.Validated);
        var submitted = NewOrder(_orders.Current, _orders.Inactive.Id, OrderStatus.Submitted);
        await _orders.Services.SaveOrdersAsync(
            validated,
            submitted,
            Rental(validated, 2),
            Rental(submitted, 1),
            NewEntry(submitted, null, ArquebusierStatus.Reserve));

        var overview = await GetOverviewAsync(_orders.Admin);

        Assert.Equal(_orders.Current.Id.ToString(), overview.GetProperty("edition").GetProperty("id").GetString());
        var rows = Rows(overview);
        Assert.Equal(
            ["Comparsa Sintética Ajena", "Comparsa Sintética Inactiva", "Comparsa Sintética Propia"],
            rows.Select(r => r.GetProperty("comparsa").GetProperty("name").GetString()));
        Assert.Equal([null, "SUBMITTED", "VALIDATED"], rows.Select(r => r.GetProperty("status").GetString()));
        Assert.Equal(JsonValueKind.Null, rows[0].GetProperty("totals").ValueKind);
        Assert.True(rows[0].GetProperty("canPrepare").GetBoolean());
        Assert.Equal((1, 1), (rows[1].GetProperty("totals").GetProperty("active").GetInt32(), rows[1].GetProperty("totals").GetProperty("reserve").GetInt32()));
        Assert.Equal(2, rows[2].GetProperty("totals").GetProperty("powderKg").GetInt32());
        var counts = overview.GetProperty("statusCounts");
        Assert.Equal(
            (1, 0, 1, 0, 1),
            (counts.GetProperty("notPrepared").GetInt32(), counts.GetProperty("draft").GetInt32(), counts.GetProperty("submitted").GetInt32(),
             counts.GetProperty("returned").GetInt32(), counts.GetProperty("validated").GetInt32()));
        var totals = overview.GetProperty("editionTotals");
        Assert.Equal((2, 1, 3), (totals.GetProperty("active").GetInt32(), totals.GetProperty("reserve").GetInt32(), totals.GetProperty("powderKg").GetInt32()));
        var rental = Assert.Single(totals.GetProperty("weaponRentals").EnumerateArray());
        Assert.Equal(
            (_orders.Offered.Label, 2),
            (rental.GetProperty("weaponModel").GetProperty("label").GetString(), rental.GetProperty("count").GetInt32()));
    }

    [Fact]
    public async Task A_FiringChief_sees_only_their_comparsas_without_Federation_totals()
    {
        await _orders.Services.SaveOrdersAsync(NewOrder(_orders.Current, _orders.Other.Id, OrderStatus.Submitted));

        var overview = await GetOverviewAsync(_orders.FiringChief);

        var rows = Rows(overview);
        Assert.Equal([_orders.Inactive.Id, _orders.Own.Id], rows.Select(r => r.GetProperty("comparsa").GetProperty("id").GetGuid()));
        Assert.Equal([false, true], rows.Select(r => r.GetProperty("canPrepare").GetBoolean()));
        Assert.Equal(JsonValueKind.Null, overview.GetProperty("statusCounts").ValueKind);
        Assert.Equal(JsonValueKind.Null, overview.GetProperty("editionTotals").ValueKind);
    }

    [Fact]
    public async Task A_FiringChief_cannot_prepare_once_the_orders_are_closed()
    {
        await _orders.SetOrdersOpenAsync(false);

        var overview = await GetOverviewAsync(_orders.FiringChief);

        Assert.All(Rows(overview), r => Assert.False(r.GetProperty("canPrepare").GetBoolean()));
    }

    [Fact]
    public async Task Another_edition_is_shown_when_asked_for()
    {
        await _orders.Services.SaveOrdersAsync(NewOrder(_orders.Previous, _orders.Own.Id, OrderStatus.Validated));

        var overview = await GetOverviewAsync(_orders.FiringChief, $"?editionId={_orders.Previous.Id}");

        Assert.Equal(2030, overview.GetProperty("edition").GetProperty("year").GetInt32());
        Assert.Equal([null, "VALIDATED"], Rows(overview).Select(r => r.GetProperty("status").GetString()));
    }

    [Fact]
    public async Task Without_an_edition_in_progress_the_overview_is_empty()
    {
        await _orders.SetCurrentStatusAsync(EditionStatus.Closed);

        var overview = await GetOverviewAsync(_orders.Admin);

        Assert.Equal(JsonValueKind.Null, overview.GetProperty("edition").ValueKind);
        Assert.Empty(Rows(overview));
    }

    [Fact]
    public async Task A_draft_edition_is_not_found_for_a_FiringChief()
    {
        var draft = NewEdition(2032, EditionStatus.Draft);
        await _orders.Services.SaveEditionsAsync(draft);

        using var response = await _orders.FiringChief.GetAsync($"/api/comparsa-orders/overview?editionId={draft.Id}", TestContext.Current.CancellationToken);
        using var unknown = await _orders.Admin.GetAsync($"/api/comparsa-orders/overview?editionId={Guid.CreateVersion7()}", TestContext.Current.CancellationToken);

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "orders.notFound");
        await AssertProblemAsync(unknown, HttpStatusCode.NotFound, "orders.notFound");
        var adminView = await GetOverviewAsync(_orders.Admin, $"?editionId={draft.Id}");
        Assert.All(Rows(adminView), r => Assert.False(r.GetProperty("canPrepare").GetBoolean()));
    }

    [Fact]
    public async Task Only_active_entries_with_warnings_count_in_the_totals()
    {
        // Synthetic arquebusiers have no licence, so both have a compliance warning.
        var (active, _) = await _orders.AddArquebusierAsync(_orders.Own.Id, "Activo Sintético");
        var (reserve, _) = await _orders.AddArquebusierAsync(_orders.Own.Id, "Reserva Sintética");
        var order = NewOrder(_orders.Current, _orders.Own.Id);
        await _orders.Services.SaveOrdersAsync(order, NewEntry(order, active.Id), NewEntry(order, reserve.Id, ArquebusierStatus.Reserve), NewEntry(order, null));

        var totals = (await OrderTestHost.GetOrderAsync(_orders.Admin, order.Id)).GetProperty("totals");
        var row = Rows(await GetOverviewAsync(_orders.Admin)).Single(r => r.GetProperty("orderId").GetString() == order.Id.ToString());

        Assert.Equal((2, 1, 1), (totals.GetProperty("active").GetInt32(), totals.GetProperty("reserve").GetInt32(), totals.GetProperty("entriesWithWarnings").GetInt32()));
        Assert.Equal(1, row.GetProperty("totals").GetProperty("entriesWithWarnings").GetInt32());
    }

    [Fact]
    public async Task Warnings_are_not_counted_outside_the_edition_in_progress()
    {
        var (arquebusier, _) = await _orders.AddArquebusierAsync(_orders.Own.Id, "Pasado Sintético");
        var order = NewOrder(_orders.Previous, _orders.Own.Id, OrderStatus.Validated);
        await _orders.Services.SaveOrdersAsync(order, NewEntry(order, arquebusier.Id));

        var overview = await GetOverviewAsync(_orders.Admin, $"?editionId={_orders.Previous.Id}");

        Assert.Equal(0, overview.GetProperty("editionTotals").GetProperty("entriesWithWarnings").GetInt32());
    }

    [Fact]
    public async Task An_edition_of_800_entries_answers_within_the_page_budget()
    {
        var own = NewOrder(_orders.Current, _orders.Own.Id);
        var other = NewOrder(_orders.Current, _orders.Other.Id);
        await _orders.Services.SaveOrdersAsync([own, other, .. Enumerable.Range(0, 800).Select(i => Rental(i % 2 == 0 ? own : other, i % 3))]);
        _ = await GetOverviewAsync(_orders.Admin);

        var watch = Stopwatch.StartNew();
        var overview = await GetOverviewAsync(_orders.Admin);
        watch.Stop();

        Assert.Equal(800, overview.GetProperty("editionTotals").GetProperty("active").GetInt32());
        Assert.True(watch.Elapsed < PageBudget, $"The overview took {watch.Elapsed}.");
    }

    private EditionEntry Rental(PolvorApp.ComparsaOrders.Orders.ComparsaOrder order, int powderKg)
    {
        var entry = NewEntry(order, null);
        (entry.PowderKg, entry.WeaponSource, entry.RentalWeaponModelId) = (powderKg, WeaponSource.Rental, _orders.Offered.Id);
        return entry;
    }

    private static async Task<JsonElement> GetOverviewAsync(HttpClient client, string query = "")
    {
        using var response = await client.GetAsync($"/api/comparsa-orders/overview{query}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadAsync<JsonElement>(response);
    }

    private static List<JsonElement> Rows(JsonElement overview) => [.. overview.GetProperty("rows").EnumerateArray()];
}
