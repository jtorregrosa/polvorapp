using PolvorApp.Billing.Contracts;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.FestivalEditions.Contracts;

namespace PolvorApp.ComparsaOrders.Totals;

/// <summary>
/// What the orders give the billing calculator (add-billing-summary, design D1 and D2): their totals as
/// quantities, the edition's prices, and whether the amounts are final.
/// </summary>
internal static class BillingMapping
{
    /// <summary>The charged items of <paramref name="totals"/>: caps of both types, rentals of every model and flask size.</summary>
    public static BillingQuantities QuantitiesOf(OrderTotals totals)
    {
        ArgumentNullException.ThrowIfNull(totals);
        return new BillingQuantities(
            totals.PowderKg,
            totals.NormalCapsBoxes + totals.SmallCapsBoxes,
            totals.WeaponRentals.Values.Sum(),
            totals.FlaskRentals1Kg + totals.FlaskRentals2Kg);
    }

    /// <summary>The edition's prices as the calculator takes them; a price not set stays null, never zero.</summary>
    public static BillingPrices PricesOf(EditionPrices prices)
    {
        ArgumentNullException.ThrowIfNull(prices);
        return new BillingPrices(prices.PowderPerKg, prices.CapsBox, prices.WeaponRental, prices.FlaskRental);
    }

    /// <summary>Final while the order is validated (spec: Provisional or final).</summary>
    public static BillingState StateOfOrder(OrderStatus status) =>
        status == OrderStatus.Validated ? BillingState.Final : BillingState.Provisional;

    /// <summary>
    /// Final when the edition has at least one prepared order and all are validated; an edition with no
    /// prepared order is provisional (spec: Edition billing (Admins)).
    /// </summary>
    public static BillingState StateOfEdition(IReadOnlyCollection<OrderStatus> statuses)
    {
        ArgumentNullException.ThrowIfNull(statuses);
        return statuses.Count > 0 && statuses.All(s => s == OrderStatus.Validated) ? BillingState.Final : BillingState.Provisional;
    }
}
