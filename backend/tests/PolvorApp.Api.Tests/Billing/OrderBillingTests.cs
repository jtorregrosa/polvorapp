using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.ComparsaOrders.Entries;
using PolvorApp.ComparsaOrders.Orders;
using PolvorApp.ComparsaOrders.Persistence;
using PolvorApp.FestivalEditions.Contracts;
using static PolvorApp.Api.Tests.Infrastructure.OrderData;

namespace PolvorApp.Api.Tests.Billing;

/// <summary>
/// The billing summary in the order response (specs: Billing summary of an order (UC-28), Billing is
/// always derived, Provisional or final, Missing prices, Billing visibility (BR-12); design D3).
/// </summary>
public sealed class OrderBillingTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private OrderTestHost _orders = null!;

    public async ValueTask InitializeAsync()
    {
        _orders = await OrderTestHost.StartAsync(postgres, mailpit);
        await _orders.SetPricesAsync(_orders.Current.Id, 55.00m, 4.50m, 30.00m, 6.00m);
    }

    public async ValueTask DisposeAsync() => await _orders.DisposeAsync();

    [Fact]
    public async Task The_order_carries_the_billing_summary_of_the_spec_example()
    {
        var order = await SaveExampleOrderAsync(_orders.Own.Id);

        var billing = await BillingAsync(_orders.FiringChief, order.Id);

        Assert.Equal(
            ["POWDER 5 × 55.00 = 275.00", "CAPS 3 × 4.50 = 13.50", "WEAPON_RENTAL 2 × 30.00 = 60.00", "FLASK_RENTAL 2 × 6.00 = 12.00"],
            Lines(billing));
        Assert.Equal(("360.50", "PROVISIONAL"), (Raw(billing.GetProperty("total")), billing.GetProperty("state").GetString()));
        Assert.Empty(billing.GetProperty("missingPrices").EnumerateArray());
    }

    [Theory]
    [InlineData(OrderStatus.Draft, "PROVISIONAL")]
    [InlineData(OrderStatus.Submitted, "PROVISIONAL")]
    [InlineData(OrderStatus.Returned, "PROVISIONAL")]
    [InlineData(OrderStatus.Validated, "FINAL")]
    public async Task The_summary_is_final_only_while_the_order_is_validated(OrderStatus status, string state)
    {
        var order = await SaveExampleOrderAsync(_orders.Own.Id);
        await UpdateOrdersAsync(db => db.Orders.Where(o => o.Id == order.Id).ExecuteUpdateAsync(
            o => o.SetProperty(x => x.Status, status).SetProperty(x => x.ReturnReason, status == OrderStatus.Returned ? "Motivo sintético" : null),
            TestContext.Current.CancellationToken));

        var billing = await BillingAsync(_orders.Admin, order.Id);

        Assert.Equal(state, billing.GetProperty("state").GetString());
    }

    [Fact]
    public async Task Entry_and_price_changes_show_on_the_next_read()
    {
        var order = await SaveExampleOrderAsync(_orders.Own.Id, OrderStatus.Validated);
        var entryId = await _orders.ReadOrdersAsync(db => db.Entries
            .Where(e => e.OrderId == order.Id && e.PowderKg == 1).Select(e => e.Id).SingleAsync(TestContext.Current.CancellationToken));

        await UpdateOrdersAsync(db => db.Entries.Where(e => e.Id == entryId)
            .ExecuteUpdateAsync(e => e.SetProperty(x => x.PowderKg, 2), TestContext.Current.CancellationToken));
        await _orders.SetPricesAsync(_orders.Current.Id, 60.00m, 4.50m, 30.00m, 6.00m);

        var billing = await BillingAsync(_orders.Admin, order.Id);

        Assert.Equal("POWDER 6 × 60.00 = 360.00", Lines(billing)[0]);
        Assert.Equal(("445.50", "FINAL"), (Raw(billing.GetProperty("total")), billing.GetProperty("state").GetString()));
    }

    [Fact]
    public async Task A_saved_entry_answers_with_the_new_billing()
    {
        await _orders.AddArquebusierAsync(_orders.Own.Id, "Con Cantimplora Sintético");
        var order = await _orders.PrepareAsync(_orders.FiringChief, _orders.Own.Id);
        var entry = order.GetProperty("entries").EnumerateArray().Single();
        Assert.Equal("FLASK_RENTAL 0 × 6.00 = 0.00", Lines(order.GetProperty("billing"))[3]);

        using var response = await _orders.FiringChief.PutAsJsonAsync(
            $"/api/comparsa-orders/{order.GetProperty("id").GetString()}/entries/{entry.GetProperty("id").GetString()}",
            new
            {
                version = entry.GetProperty("version").GetUInt32(),
                status = "ACTIVE",
                powderKg = 1,
                capsBoxes = 0,
                weaponSource = "NONE",
                flask = "RENTAL_1KG",
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var billing = (await IdentityAssertions.ReadAsync<JsonElement>(response)).GetProperty("billing");
        Assert.Equal("FLASK_RENTAL 1 × 6.00 = 6.00", Lines(billing)[3]);
        Assert.Equal("61.00", Raw(billing.GetProperty("total")));
    }

    [Fact]
    public async Task An_entry_of_an_arquebusier_no_longer_in_the_registry_still_counts()
    {
        var past = NewOrder(_orders.Previous, _orders.Own.Id, OrderStatus.Validated);
        var history = NewEntry(past, arquebusierId: null);
        history.PowderKg = 2;
        await _orders.Services.SaveOrdersAsync(past, history);
        await _orders.SetPricesAsync(_orders.Previous.Id, 50.00m, 4.00m, 25.00m, 5.00m);

        var billing = await BillingAsync(_orders.FiringChief, past.Id);

        Assert.Equal("POWDER 2 × 50.00 = 100.00", Lines(billing)[0]);
        Assert.Equal("100.00", Raw(billing.GetProperty("total")));
    }

    [Fact]
    public async Task An_entry_removed_with_its_arquebusier_no_longer_counts()
    {
        var (leaving, _) = await _orders.AddArquebusierAsync(_orders.Own.Id, "Se Va Facturación");
        var order = NewOrder(_orders.Current, _orders.Own.Id);
        var entry = NewEntry(order, leaving.Id);
        entry.PowderKg = 2;
        await _orders.Services.SaveOrdersAsync(order, entry);

        using (var deletion = await _orders.Admin.DeleteAsync($"/api/arquebusiers/{leaving.Id}", TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.NoContent, deletion.StatusCode);
        }

        var billing = await BillingAsync(_orders.Admin, order.Id);
        Assert.Equal("POWDER 0 × 55.00 = 0.00", Lines(billing)[0]);
    }

    [Fact]
    public async Task A_missing_price_leaves_the_summary_without_total()
    {
        var order = await SaveExampleOrderAsync(_orders.Own.Id);
        await _orders.SetCurrentStatusAsync(EditionStatus.Draft);
        await _orders.SetPricesAsync(_orders.Current.Id, 55.00m, 4.50m, 30.00m, null);

        var billing = await BillingAsync(_orders.Admin, order.Id);

        Assert.Equal("FLASK_RENTAL 2 × null = null", Lines(billing)[3]);
        Assert.Equal(JsonValueKind.Null, billing.GetProperty("total").ValueKind);
        Assert.Equal(["FLASK_RENTAL"], billing.GetProperty("missingPrices").EnumerateArray().Select(c => c.GetString()));
    }

    [Fact]
    public async Task An_order_with_nothing_charged_owes_zero()
    {
        var order = NewOrder(_orders.Current, _orders.Own.Id);
        var loaned = NewEntry(order, null);
        (loaned.WeaponSource, loaned.Flask) = (WeaponSource.Loan, FlaskOption.Owned);
        await _orders.Services.SaveOrdersAsync(order, loaned, NewEntry(order, null, ArquebusierStatus.Reserve));

        var billing = await BillingAsync(_orders.FiringChief, order.Id);

        Assert.Equal(
            ["POWDER 0 × 55.00 = 0.00", "CAPS 0 × 4.50 = 0.00", "WEAPON_RENTAL 0 × 30.00 = 0.00", "FLASK_RENTAL 0 × 6.00 = 0.00"],
            Lines(billing));
        Assert.Equal("0.00", Raw(billing.GetProperty("total")));
    }

    [Fact]
    public async Task A_FiringChief_gets_no_billing_of_a_draft_edition()
    {
        var order = await SaveExampleOrderAsync(_orders.Own.Id);
        await _orders.SetCurrentStatusAsync(EditionStatus.Draft);

        using var response = await _orders.FiringChief.GetAsync($"/api/comparsa-orders/{order.Id}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("billing", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Another_comparsas_order_reveals_no_billing()
    {
        var order = await SaveExampleOrderAsync(_orders.Other.Id);

        using var response = await _orders.FiringChief.GetAsync($"/api/comparsa-orders/{order.Id}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("billing", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The order of the spec's example: one <c>RESERVE</c> entry and four <c>ACTIVE</c> ones; 5 kg, 3 caps
    /// boxes, 2 rented weapons and 2 rented flasks, a loaned weapon and an owned flask.
    /// </summary>
    private async Task<ComparsaOrder> SaveExampleOrderAsync(Guid comparsaId, OrderStatus status = OrderStatus.Draft)
    {
        var order = NewOrder(_orders.Current, comparsaId, status);
        var full = NewEntry(order, null);
        (full.PowderKg, full.CapsBoxes, full.CapsType, full.WeaponSource, full.RentalWeaponModelId, full.Flask) =
            (2, 2, CapsType.Normal, WeaponSource.Rental, _orders.Offered.Id, FlaskOption.Rental2Kg);
        var small = NewEntry(order, null);
        (small.PowderKg, small.CapsBoxes, small.CapsType, small.Flask) = (1, 1, CapsType.Small, FlaskOption.Rental1Kg);
        var loaned = NewEntry(order, null);
        (loaned.PowderKg, loaned.WeaponSource, loaned.Flask) = (2, WeaponSource.Loan, FlaskOption.Owned);
        var captain = NewEntry(order, null);
        (captain.WeaponSource, captain.RentalWeaponModelId) = (WeaponSource.Rental, _orders.Offered.Id);
        await _orders.Services.SaveOrdersAsync(order, full, small, loaned, captain, NewEntry(order, null, ArquebusierStatus.Reserve));
        return order;
    }

    private async Task UpdateOrdersAsync(Func<ComparsaOrdersDbContext, Task<int>> update)
    {
        await using var scope = _orders.Services.CreateAsyncScope();
        await update(scope.ServiceProvider.GetRequiredService<ComparsaOrdersDbContext>());
    }

    private static async Task<JsonElement> BillingAsync(HttpClient client, Guid orderId) =>
        (await OrderTestHost.GetOrderAsync(client, orderId)).GetProperty("billing");

    /// <summary>The lines as "CONCEPT quantity × unitPrice = amount", with the numbers as the JSON wrote them.</summary>
    private static string[] Lines(JsonElement billing) =>
        [.. billing.GetProperty("lines").EnumerateArray().Select(l =>
            $"{l.GetProperty("concept").GetString()} {l.GetProperty("quantity").GetInt32().ToString(CultureInfo.InvariantCulture)}"
            + $" × {Raw(l.GetProperty("unitPrice"))} = {Raw(l.GetProperty("amount"))}")];

    /// <summary>A JSON number exactly as written (so "275" and "275.00" differ), or "null".</summary>
    private static string Raw(JsonElement value) => value.ValueKind == JsonValueKind.Null ? "null" : value.GetRawText();
}
