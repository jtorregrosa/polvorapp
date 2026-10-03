using PolvorApp.FestivalEditions.Contracts;
using PolvorApp.FestivalEditions.Editions;

namespace PolvorApp.Api.Tests.Editions;

/// <summary>
/// Spec "Festival editions (UC-10)" and "Edition prices": every blocking field rule is reported by
/// field name with a reason code, all at once.
/// </summary>
public sealed class EditionInputTests
{
    private static readonly EditionFields Valid = new(
        FestivalStartsOn: "2031-04-22",
        FestivalEndsOn: "2031-04-25",
        OrdersOpenOn: "2031-01-10",
        OrdersCloseOn: "2031-02-10",
        Prices: new PriceFields(55.00m, 4.50m, 30m, 6m));

    [Fact]
    public void Valid_fields_are_read()
    {
        var (input, errors) = EditionInput.Read(2031, Valid);

        Assert.Empty(errors);
        Assert.Equal(
            new EditionInput(
                new DateOnly(2031, 4, 22),
                new DateOnly(2031, 4, 25),
                new DateOnly(2031, 1, 10),
                new DateOnly(2031, 2, 10),
                new EditionPrices(55.00m, 4.50m, 30m, 6m)),
            input);
    }

    [Fact]
    public void Window_dates_and_prices_are_optional()
    {
        var (input, errors) = EditionInput.Read(2031, Valid with { OrdersOpenOn = null, OrdersCloseOn = "", Prices = null });

        Assert.Empty(errors);
        Assert.Equal((null, null, EditionPrices.None), (input!.OrdersOpenOn, input.OrdersCloseOn, input.Prices));
    }

    [Theory]
    [InlineData(null, "required")]
    [InlineData(1999, "outOfRange")]
    [InlineData(2101, "outOfRange")]
    public void An_invalid_year_is_named(int? year, string reason)
    {
        var (_, errors) = EditionInput.ReadYear(year);

        Assert.Equal(new Dictionary<string, string> { ["year"] = reason }, errors);
    }

    [Theory]
    [InlineData(2000)]
    [InlineData(2100)]
    public void The_year_range_is_inclusive(int year) => Assert.Empty(EditionInput.ReadYear(year).Errors);

    [Fact]
    public void Festival_dates_are_required()
    {
        var (_, errors) = EditionInput.Read(2031, Valid with { FestivalStartsOn = null, FestivalEndsOn = " " });

        Assert.Equal("required", errors["festivalStartsOn"]);
        Assert.Equal("invalid", errors["festivalEndsOn"]);
    }

    [Theory]
    [InlineData("2030-12-30", "2031-04-25", "festivalStartsOn")]
    [InlineData("2031-04-22", "2032-01-01", "festivalEndsOn")]
    public void Festival_dates_outside_the_year_are_blocking(string starts, string ends, string field)
    {
        var (_, errors) = EditionInput.Read(2031, Valid with { FestivalStartsOn = starts, FestivalEndsOn = ends });

        Assert.Equal("outsideYear", errors[field]);
    }

    [Fact]
    public void The_festival_cannot_end_before_it_starts()
    {
        var (_, errors) = EditionInput.Read(2031, Valid with { FestivalStartsOn = "2031-04-25", FestivalEndsOn = "2031-04-22" });

        Assert.Equal(new Dictionary<string, string> { ["festivalEndsOn"] = "beforeStart" }, errors);
    }

    [Fact]
    public void A_one_day_festival_is_valid() =>
        Assert.Empty(EditionInput.Read(2031, Valid with { FestivalEndsOn = "2031-04-22" }).Errors);

    [Fact]
    public void Orders_cannot_close_before_they_open()
    {
        var (_, errors) = EditionInput.Read(2031, Valid with { OrdersOpenOn = "2031-02-10", OrdersCloseOn = "2031-02-01" });

        Assert.Equal(new Dictionary<string, string> { ["ordersCloseOn"] = "beforeOpen" }, errors);
    }

    [Fact]
    public void The_window_may_open_and_close_on_the_same_day() =>
        Assert.Empty(EditionInput.Read(2031, Valid with { OrdersOpenOn = "2031-02-10", OrdersCloseOn = "2031-02-10" }).Errors);

    [Theory]
    [InlineData("ordersOpenOn")]
    [InlineData("ordersCloseOn")]
    public void A_window_date_after_the_festival_start_is_blocking(string field)
    {
        var fields = field == "ordersOpenOn"
            ? Valid with { OrdersOpenOn = "2031-04-23", OrdersCloseOn = null }
            : Valid with { OrdersCloseOn = "2031-04-23" };

        var (_, errors) = EditionInput.Read(2031, fields);

        Assert.Equal(new Dictionary<string, string> { [field] = "afterFestival" }, errors);
    }

    [Fact]
    public void A_window_closing_on_the_first_festival_day_is_valid() =>
        Assert.Empty(EditionInput.Read(2031, Valid with { OrdersCloseOn = "2031-04-22" }).Errors);

    [Fact]
    public void A_malformed_window_date_is_invalid()
    {
        var (_, errors) = EditionInput.Read(2031, Valid with { OrdersOpenOn = "10/01/2031" });

        Assert.Equal(new Dictionary<string, string> { ["ordersOpenOn"] = "invalid" }, errors);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("0.01")]
    [InlineData("9999.99")]
    [InlineData("4.50")]
    [InlineData("4.500")]
    public void Prices_within_range_and_two_decimals_are_valid(string amount)
    {
        var value = decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture);

        var (input, errors) = EditionInput.Read(2031, Valid with { Prices = new PriceFields(value, value, value, value) });

        Assert.Empty(errors);
        Assert.Equal(value, input!.Prices.CapsBox);
    }

    [Theory]
    [InlineData("-1", "outOfRange")]
    [InlineData("-0.01", "outOfRange")]
    [InlineData("10000", "outOfRange")]
    [InlineData("4.555", "decimals")]
    public void An_invalid_price_is_named(string amount, string reason)
    {
        var value = decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture);

        var (_, errors) = EditionInput.Read(2031, Valid with { Prices = new PriceFields(55m, value, 30m, 6m) });

        Assert.Equal(new Dictionary<string, string> { ["prices.capsBox"] = reason }, errors);
    }

    [Fact]
    public void Every_error_is_reported_at_once()
    {
        var (input, errors) = EditionInput.Read(2031, new EditionFields(
            FestivalStartsOn: "2030-04-22",
            FestivalEndsOn: null,
            OrdersOpenOn: "2031-02-10",
            OrdersCloseOn: "2031-02-01",
            Prices: new PriceFields(-1m, 4.555m, null, 10000m)));

        Assert.Null(input);
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["festivalStartsOn"] = "outsideYear",
                ["festivalEndsOn"] = "required",
                ["ordersCloseOn"] = "beforeOpen",
                ["prices.powderPerKg"] = "outOfRange",
                ["prices.capsBox"] = "decimals",
                ["prices.flaskRental"] = "outOfRange",
            },
            errors);
    }
}
