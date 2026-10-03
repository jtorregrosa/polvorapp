namespace PolvorApp.FestivalEditions.Contracts;

/// <summary>
/// The four flat prices of an edition (glossary: <c>EditionPrices</c>; spec: Edition prices), in euros
/// with at most two decimals. Each is optional while the edition is a draft; once it has left draft all
/// four are set. Other modules read them through <see cref="EditionSnapshot.Prices"/>, e.g. billing
/// (add-billing-summary, design D2).
/// </summary>
public sealed record EditionPrices(decimal? PowderPerKg, decimal? CapsBox, decimal? WeaponRental, decimal? FlaskRental)
{
    /// <summary>No price set yet.</summary>
    public static readonly EditionPrices None = new(null, null, null, null);
}
