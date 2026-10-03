using System.Globalization;
using PolvorApp.Billing;
using PolvorApp.Billing.Contracts;

namespace PolvorApp.Api.Tests.Billing;

/// <summary>
/// The billing summary (spec: Billing summary of an order (UC-28), Provisional or final, Missing
/// prices, Edition billing (Admins)), with the prices of the spec's example: 55.00, 4.50, 30.00 and 6.00.
/// Amounts are compared as invariant text, so a lost scale ("275" instead of "275.00") fails.
/// </summary>
public sealed class BillingSummariseTests
{
    private static readonly BillingCalculator Calculator = new();
    private static readonly BillingPrices Prices = new(55.00m, 4.50m, 30.00m, 6.00m);

    /// <summary>The order of the spec's example: 5 kg, 3 caps boxes, 2 rented weapons and 2 rented flasks; 360.50.</summary>
    private static readonly BillingQuantities Example = new(PowderKg: 5, CapsBoxes: 3, WeaponRentals: 2, FlaskRentals: 2);

    private static string Text(decimal? amount) => amount?.ToString(CultureInfo.InvariantCulture) ?? "null";

    private static string[] Lines(BillingSummary summary) =>
        [.. summary.Lines.Select(l => $"{l.Concept} {l.Quantity} × {Text(l.UnitPrice)} = {Text(l.Amount)}")];

    private static BillingSummary Summarise(BillingQuantities quantities, BillingPrices? prices = null) =>
        Calculator.Summarise(quantities, prices ?? Prices, BillingState.Provisional);

    [Fact]
    public void Summary_of_the_spec_example_line_by_line()
    {
        var summary = Summarise(Example);

        Assert.Equal(
            [
                "Powder 5 × 55.00 = 275.00",
                "Caps 3 × 4.50 = 13.50",
                "WeaponRental 2 × 30.00 = 60.00",
                "FlaskRental 2 × 6.00 = 12.00",
            ],
            Lines(summary));
        Assert.Equal("360.50", Text(summary.Total));
        Assert.Empty(summary.MissingPrices);
    }

    [Fact]
    public void Amounts_are_exact()
    {
        var summary = Summarise(new BillingQuantities(0, 3, 0, 0), Prices with { CapsBox = 0.10m });

        Assert.Equal("Caps 3 × 0.10 = 0.30", Lines(summary)[1]);
        Assert.Equal("0.30", Text(summary.Total));
    }

    [Fact]
    public void Prices_without_decimals_still_answer_two_decimals()
    {
        var summary = Summarise(new BillingQuantities(1, 0, 0, 0), new BillingPrices(55m, 4m, 30m, 6m));

        Assert.Equal(
            ["Powder 1 × 55.00 = 55.00", "Caps 0 × 4.00 = 0.00", "WeaponRental 0 × 30.00 = 0.00", "FlaskRental 0 × 6.00 = 0.00"],
            Lines(summary));
        Assert.Equal("55.00", Text(summary.Total));
    }

    [Fact]
    public void Nothing_charged_is_zero_everywhere()
    {
        var summary = Summarise(new BillingQuantities(0, 0, 0, 0));

        Assert.All(summary.Lines, line => Assert.Equal((0, "0.00"), (line.Quantity, Text(line.Amount))));
        Assert.Equal("0.00", Text(summary.Total));
    }

    [Fact]
    public void Lines_are_always_in_concept_order() =>
        Assert.Equal(
            [BillingConcept.Powder, BillingConcept.Caps, BillingConcept.WeaponRental, BillingConcept.FlaskRental],
            Summarise(Example).Lines.Select(l => l.Concept));

    [Theory]
    [InlineData(BillingState.Provisional)]
    [InlineData(BillingState.Final)]
    public void State_is_the_one_given(BillingState state) =>
        Assert.Equal(state, Calculator.Summarise(Example, Prices, state).State);

    [Fact]
    public void A_missing_price_leaves_its_quantity_without_amount_and_no_total()
    {
        var summary = Summarise(Example, Prices with { FlaskRental = null });

        Assert.Equal("FlaskRental 2 × null = null", Lines(summary)[3]);
        Assert.Equal("Powder 5 × 55.00 = 275.00", Lines(summary)[0]);
        Assert.Null(summary.Total);
        Assert.Equal([BillingConcept.FlaskRental], summary.MissingPrices);
    }

    [Fact]
    public void Several_missing_prices_are_named_in_line_order()
    {
        var summary = Calculator.Summarise(Example, new BillingPrices(null, 4.50m, 30.00m, null), BillingState.Final);

        Assert.Equal([BillingConcept.Powder, BillingConcept.FlaskRental], summary.MissingPrices);
        Assert.Null(summary.Total);
        Assert.Equal(BillingState.Final, summary.State);
    }

    [Fact]
    public void Every_price_missing()
    {
        var summary = Summarise(Example, new BillingPrices(null, null, null, null));

        Assert.Equal(4, summary.MissingPrices.Count);
        Assert.All(summary.Lines, line => Assert.Null(line.Amount));
        Assert.Null(summary.Total);
    }

    [Fact]
    public void A_missing_price_removes_the_total_even_when_nothing_uses_it()
    {
        var summary = Summarise(new BillingQuantities(1, 0, 0, 0), Prices with { CapsBox = null });

        Assert.Equal([BillingConcept.Caps], summary.MissingPrices);
        Assert.Null(summary.Total);
    }

    [Fact]
    public void Summed_quantities_give_the_sum_of_the_order_totals()
    {
        BillingQuantities[] orders = [Example, new(1, 0, 2, 0), new(2, 7, 0, 1)];
        var edition = new BillingQuantities(
            orders.Sum(o => o.PowderKg), orders.Sum(o => o.CapsBoxes), orders.Sum(o => o.WeaponRentals), orders.Sum(o => o.FlaskRentals));

        Assert.Equal(orders.Sum(o => Summarise(o).Total!.Value), Summarise(edition).Total);
    }

    [Fact]
    public void Largest_edition_fits_exactly()
    {
        // D4: ~1,000 entries with 2 kg, 99 caps boxes, a rented weapon and a rented flask, at the highest price.
        var summary = Summarise(new BillingQuantities(2_000, 99_000, 1_000, 1_000), new BillingPrices(9999.99m, 9999.99m, 9999.99m, 9999.99m));

        Assert.Equal("1029998970.00", Text(summary.Total));
    }

    [Theory]
    [InlineData(-1, 0, 0, 0)]
    [InlineData(0, -1, 0, 0)]
    [InlineData(0, 0, -1, 0)]
    [InlineData(0, 0, 0, -1)]
    public void Negative_quantities_are_refused(int powder, int caps, int weapons, int flasks) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Summarise(new BillingQuantities(powder, caps, weapons, flasks)));
}
