using PolvorApp.Billing.Contracts;

namespace PolvorApp.Billing;

/// <inheritdoc />
internal sealed class BillingCalculator : IBillingCalculator
{
    public BillingSummary Summarise(BillingQuantities quantities, BillingPrices prices, BillingState state)
    {
        ArgumentNullException.ThrowIfNull(quantities);
        ArgumentNullException.ThrowIfNull(prices);
        ArgumentOutOfRangeException.ThrowIfNegative(quantities.PowderKg);
        ArgumentOutOfRangeException.ThrowIfNegative(quantities.CapsBoxes);
        ArgumentOutOfRangeException.ThrowIfNegative(quantities.WeaponRentals);
        ArgumentOutOfRangeException.ThrowIfNegative(quantities.FlaskRentals);
        BillingLine[] lines =
        [
            Line(BillingConcept.Powder, quantities.PowderKg, prices.PowderPerKg),
            Line(BillingConcept.Caps, quantities.CapsBoxes, prices.CapsBox),
            Line(BillingConcept.WeaponRental, quantities.WeaponRentals, prices.WeaponRental),
            Line(BillingConcept.FlaskRental, quantities.FlaskRentals, prices.FlaskRental),
        ];
        BillingConcept[] missing = [.. lines.Where(l => l.UnitPrice is null).Select(l => l.Concept)];
        // Every line has an amount exactly when no price is missing: a missing price is never read as zero.
        decimal? total = missing.Length == 0 ? Money(lines.Sum(l => l.Amount!.Value)) : null;
        return new BillingSummary(lines, total, state, missing);
    }

    private static BillingLine Line(BillingConcept concept, int quantity, decimal? unitPrice) =>
        unitPrice is { } price ? new(concept, quantity, Money(price), Money(quantity * price)) : new(concept, quantity, null, null);

    /// <summary>
    /// Exact euros written with at least two decimals: prices have at most two (<c>numeric(6,2)</c>) and
    /// quantities are whole, so nothing is rounded; adding <c>0.00m</c> only raises the scale, so
    /// <c>0</c> reads <c>0.00</c> once serialised.
    /// </summary>
    private static decimal Money(decimal amount) => amount + 0.00m;
}
