using PolvorApp.Billing.Contracts;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.ComparsaOrders.Totals;
using PolvorApp.FestivalEditions.Contracts;

namespace PolvorApp.Api.Tests.Billing;

/// <summary>What the orders give the calculator (design D1, D2): quantities from the totals, prices and states.</summary>
public sealed class BillingMappingTests
{
    [Fact]
    public void Quantities_add_caps_of_both_types_rentals_of_every_model_and_flasks_of_both_sizes()
    {
        var totals = OrderTotals.Zero with
        {
            PowderKg = 5,
            NormalCapsBoxes = 2,
            SmallCapsBoxes = 1,
            WeaponRentals = new Dictionary<Guid, int> { [Guid.CreateVersion7()] = 2, [Guid.CreateVersion7()] = 1 },
            FlaskRentals1Kg = 1,
            FlaskRentals2Kg = 3,
        };

        Assert.Equal(new BillingQuantities(5, 3, 3, 4), BillingMapping.QuantitiesOf(totals));
    }

    [Fact]
    public void Prices_not_set_stay_null() =>
        Assert.Equal(
            new BillingPrices(55.00m, null, 30.00m, null),
            BillingMapping.PricesOf(new EditionPrices(55.00m, null, 30.00m, null)));

    [Theory]
    [InlineData(OrderStatus.Draft, BillingState.Provisional)]
    [InlineData(OrderStatus.Submitted, BillingState.Provisional)]
    [InlineData(OrderStatus.Returned, BillingState.Provisional)]
    [InlineData(OrderStatus.Validated, BillingState.Final)]
    public void An_order_is_final_only_while_validated(OrderStatus status, BillingState state) =>
        Assert.Equal(state, BillingMapping.StateOfOrder(status));

    [Fact]
    public void An_edition_is_final_only_with_prepared_orders_all_validated()
    {
        Assert.Equal(BillingState.Provisional, BillingMapping.StateOfEdition([]));
        Assert.Equal(BillingState.Provisional, BillingMapping.StateOfEdition([OrderStatus.Validated, OrderStatus.Submitted]));
        Assert.Equal(BillingState.Final, BillingMapping.StateOfEdition([OrderStatus.Validated, OrderStatus.Validated]));
    }
}
