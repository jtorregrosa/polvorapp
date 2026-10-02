using PolvorApp.SharedKernel.Validation;

namespace PolvorApp.FestivalEditions.Editions;

/// <summary>The four flat prices as received, in euros; null is not set yet.</summary>
internal sealed record PriceFields(decimal? PowderPerKg, decimal? CapsBox, decimal? WeaponRental, decimal? FlaskRental);

/// <summary>The edition fields an Admin edits, as received: dates arrive as text so errors name the field.</summary>
internal sealed record EditionFields(
    string? FestivalStartsOn,
    string? FestivalEndsOn,
    string? OrdersOpenOn,
    string? OrdersCloseOn,
    PriceFields? Prices);

/// <summary>Validated prices (<c>EditionPrices</c>); each is optional while the edition is a draft.</summary>
internal sealed record EditionPrices(decimal? PowderPerKg, decimal? CapsBox, decimal? WeaponRental, decimal? FlaskRental)
{
    public static readonly EditionPrices None = new(null, null, null, null);

    public static EditionPrices Of(FestivalEdition edition) =>
        new(edition.PowderPerKg, edition.CapsBox, edition.WeaponRental, edition.FlaskRental);
}

/// <summary>Validated edition fields, ready to store.</summary>
internal sealed record EditionInput(
    DateOnly FestivalStartsOn,
    DateOnly FestivalEndsOn,
    DateOnly? OrdersOpenOn,
    DateOnly? OrdersCloseOn,
    EditionPrices Prices)
{
    public const string OutOfRange = "outOfRange";
    public const string OutsideYear = "outsideYear";
    public const string BeforeStart = "beforeStart";
    public const string BeforeOpen = "beforeOpen";
    public const string AfterFestival = "afterFestival";
    public const string Decimals = "decimals";

    /// <summary>Highest price: <c>numeric(6,2)</c> (spec: Edition prices).</summary>
    public const decimal MaxPrice = 9999.99m;

    /// <summary>The year of a new edition (spec: Festival editions (UC-10)).</summary>
    public static (int? Year, IReadOnlyDictionary<string, string> Errors) ReadYear(int? year)
    {
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        if (year is null)
        {
            errors["year"] = InputFields.Required;
        }
        else if (year is < FestivalEdition.MinYear or > FestivalEdition.MaxYear)
        {
            errors["year"] = OutOfRange;
        }

        return (errors.Count == 0 ? year : null, errors);
    }

    /// <summary>
    /// Validates <paramref name="fields"/> for the edition of <paramref name="year"/>. Every invalid
    /// field is reported at once; prices as <c>prices.&lt;field&gt;</c>. Window dates are compared
    /// with the festival start only when that date is itself valid, so one mistake is reported once.
    /// </summary>
    public static (EditionInput? Input, IReadOnlyDictionary<string, string> Errors) Read(int year, EditionFields fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);

        var starts = InYear(InputFields.RequiredDate(fields.FestivalStartsOn, "festivalStartsOn", errors), year, "festivalStartsOn", errors);
        var ends = InYear(InputFields.RequiredDate(fields.FestivalEndsOn, "festivalEndsOn", errors), year, "festivalEndsOn", errors);
        if (starts is { } first && ends is { } last && last < first)
        {
            errors["festivalEndsOn"] = BeforeStart;
        }

        var opens = NotAfter(InputFields.OptionalDate(fields.OrdersOpenOn, "ordersOpenOn", errors), starts, "ordersOpenOn", errors);
        var closes = NotAfter(InputFields.OptionalDate(fields.OrdersCloseOn, "ordersCloseOn", errors), starts, "ordersCloseOn", errors);
        if (opens is { } open && closes is { } close && close < open)
        {
            errors["ordersCloseOn"] = BeforeOpen;
        }

        var prices = fields.Prices ?? new PriceFields(null, null, null, null);
        var read = new EditionPrices(
            Price(prices.PowderPerKg, "prices.powderPerKg", errors),
            Price(prices.CapsBox, "prices.capsBox", errors),
            Price(prices.WeaponRental, "prices.weaponRental", errors),
            Price(prices.FlaskRental, "prices.flaskRental", errors));

        if (errors.Count > 0 || starts is null || ends is null)
        {
            return (null, errors);
        }

        return (new EditionInput(starts.Value, ends.Value, opens, closes, read), errors);
    }

    private static DateOnly? InYear(DateOnly? date, int year, string field, Dictionary<string, string> errors)
    {
        if (date is { } value && value.Year != year)
        {
            errors[field] = OutsideYear;
            return null;
        }

        return date;
    }

    private static DateOnly? NotAfter(DateOnly? date, DateOnly? festivalStartsOn, string field, Dictionary<string, string> errors)
    {
        if (date is { } value && festivalStartsOn is { } starts && value > starts)
        {
            errors[field] = AfterFestival;
            return null;
        }

        return date;
    }

    /// <summary>A price from 0.00 to 9999.99 with at most two decimals; never rounded (spec: Edition prices).</summary>
    private static decimal? Price(decimal? price, string field, Dictionary<string, string> errors)
    {
        if (price is not { } value)
        {
            return null;
        }

        if (value is < 0 or > MaxPrice)
        {
            errors[field] = OutOfRange;
            return null;
        }

        if (decimal.Round(value, 2) != value)
        {
            errors[field] = Decimals;
            return null;
        }

        return value;
    }
}
