using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.AuditPrivacy.Persistence;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.ComparsaOrders.Entries;
using PolvorApp.SharedKernel.Auditing;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;
using static PolvorApp.Api.Tests.Infrastructure.OrderData;

namespace PolvorApp.Api.Tests.Billing;

/// <summary>
/// The billing in the orders overview (specs: Edition billing (Admins), Billing visibility (BR-12);
/// design D3): each prepared row's summary and, for Admins, the edition billing.
/// </summary>
public sealed class OverviewBillingTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private OrderTestHost _orders = null!;

    public async ValueTask InitializeAsync()
    {
        _orders = await OrderTestHost.StartAsync(postgres, mailpit);
        await _orders.SetPricesAsync(_orders.Current.Id, 55.00m, 4.50m, 30.00m, 6.00m);
    }

    public async ValueTask DisposeAsync() => await _orders.DisposeAsync();

    [Fact]
    public async Task An_Admin_sees_each_rows_billing_and_the_provisional_edition_billing()
    {
        await SaveOrdersAsync(OrderStatus.Validated, OrderStatus.Submitted);

        var overview = await GetOverviewAsync(_orders.Admin);

        Assert.Equal(JsonValueKind.Null, Row(overview, _orders.Other.Id).GetProperty("billing").ValueKind);
        Assert.Equal(("140.00", "PROVISIONAL"), Total(Row(overview, _orders.Inactive.Id).GetProperty("billing")));
        Assert.Equal(("360.50", "FINAL"), Total(Row(overview, _orders.Own.Id).GetProperty("billing")));
        var edition = overview.GetProperty("editionBilling");
        Assert.Equal(("500.50", "PROVISIONAL"), Total(edition));
        Assert.Equal(7, edition.GetProperty("lines")[0].GetProperty("quantity").GetInt32());
    }

    [Fact]
    public async Task The_edition_billing_is_final_when_every_prepared_order_is_validated()
    {
        await SaveOrdersAsync(OrderStatus.Validated, OrderStatus.Validated);

        var overview = await GetOverviewAsync(_orders.Admin);

        Assert.Equal(("500.50", "FINAL"), Total(overview.GetProperty("editionBilling")));
    }

    [Fact]
    public async Task With_no_prepared_order_the_edition_billing_is_zero_and_provisional()
    {
        var overview = await GetOverviewAsync(_orders.Admin);

        Assert.Equal(("0.00", "PROVISIONAL"), Total(overview.GetProperty("editionBilling")));
    }

    [Fact]
    public async Task A_FiringChief_sees_only_their_rows_billing_and_no_edition_billing()
    {
        await SaveOrdersAsync(OrderStatus.Validated, OrderStatus.Submitted);

        var overview = await GetOverviewAsync(_orders.FiringChief);

        Assert.Equal(
            new[] { _orders.Inactive.Id, _orders.Own.Id }.Order(),
            Rows(overview).Select(r => r.GetProperty("comparsa").GetProperty("id").GetGuid()).Order());
        Assert.Equal(("140.00", "PROVISIONAL"), Total(Row(overview, _orders.Inactive.Id).GetProperty("billing")));
        Assert.Equal(("360.50", "FINAL"), Total(Row(overview, _orders.Own.Id).GetProperty("billing")));
        Assert.Equal(JsonValueKind.Null, overview.GetProperty("editionBilling").ValueKind);
    }

    [Fact]
    public async Task A_missing_price_leaves_the_rows_and_the_edition_without_total()
    {
        await SaveOrdersAsync(OrderStatus.Draft, OrderStatus.Draft);
        await _orders.SetCurrentStatusAsync(PolvorApp.FestivalEditions.Contracts.EditionStatus.Draft);
        await _orders.SetPricesAsync(_orders.Current.Id, 55.00m, null, 30.00m, 6.00m);

        var overview = await GetOverviewAsync(_orders.Admin, $"?editionId={_orders.Current.Id}");

        foreach (var billing in new[] { Row(overview, _orders.Own.Id).GetProperty("billing"), overview.GetProperty("editionBilling") })
        {
            Assert.Equal(JsonValueKind.Null, billing.GetProperty("total").ValueKind);
            Assert.Equal(["CAPS"], billing.GetProperty("missingPrices").EnumerateArray().Select(c => c.GetString()));
        }
    }

    [Fact]
    public async Task Reading_the_billing_records_no_audit_entry()
    {
        await SaveOrdersAsync(OrderStatus.Validated, OrderStatus.Submitted);
        var before = await AuditCountAsync();

        var overview = await GetOverviewAsync(_orders.FiringChief);
        await GetOverviewAsync(_orders.Admin);
        await OrderTestHost.GetOrderAsync(_orders.FiringChief, Guid.Parse(Row(overview, _orders.Own.Id).GetProperty("orderId").GetString()!));

        Assert.Equal(before, await AuditCountAsync());
    }

    private async Task<int> AuditCountAsync()
    {
        await using var scope = _orders.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AuditDbContext>().Set<AuditEntry>()
            .CountAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The own comparsa's order owes 360.50 (the spec's example: 5 kg, 3 caps boxes, 2 weapon and 2 flask
    /// rentals); the inactive comparsa's owes 140.00 (2 kg and a rented weapon). The other comparsa has none.
    /// </summary>
    private async Task SaveOrdersAsync(OrderStatus own, OrderStatus inactive)
    {
        var example = NewOrder(_orders.Current, _orders.Own.Id, own);
        var full = NewEntry(example, null);
        (full.PowderKg, full.CapsBoxes, full.CapsType, full.WeaponSource, full.RentalWeaponModelId, full.Flask) =
            (2, 2, CapsType.Normal, WeaponSource.Rental, _orders.Offered.Id, FlaskOption.Rental2Kg);
        var small = NewEntry(example, null);
        (small.PowderKg, small.CapsBoxes, small.CapsType, small.Flask) = (1, 1, CapsType.Small, FlaskOption.Rental1Kg);
        var loaned = NewEntry(example, null);
        (loaned.PowderKg, loaned.WeaponSource) = (2, WeaponSource.Loan);
        var captain = NewEntry(example, null);
        (captain.WeaponSource, captain.RentalWeaponModelId) = (WeaponSource.Rental, _orders.Offered.Id);

        var other = NewOrder(_orders.Current, _orders.Inactive.Id, inactive);
        var second = NewEntry(other, null);
        (second.PowderKg, second.WeaponSource, second.RentalWeaponModelId) = (2, WeaponSource.Rental, _orders.Offered.Id);

        await _orders.Services.SaveOrdersAsync(example, full, small, loaned, captain, other, second);
    }

    private static (string Total, string? State) Total(JsonElement billing) =>
        (billing.GetProperty("total").GetRawText(), billing.GetProperty("state").GetString());

    private static async Task<JsonElement> GetOverviewAsync(HttpClient client, string query = "")
    {
        using var response = await client.GetAsync($"/api/comparsa-orders/overview{query}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadAsync<JsonElement>(response);
    }

    private static List<JsonElement> Rows(JsonElement overview) => [.. overview.GetProperty("rows").EnumerateArray()];

    private static JsonElement Row(JsonElement overview, Guid comparsaId) =>
        Rows(overview).Single(r => r.GetProperty("comparsa").GetProperty("id").GetGuid() == comparsaId);
}
