namespace PolvorApp.ComparsaOrders.Contracts;

/// <summary>
/// The participation of every started edition, counted from the entries of its prepared orders
/// (spec: Edition trends (UC-07); add-statistics-trends, design D1). Server-side only: it applies the
/// comparsas it is given and no other rule, so callers pass only comparsas the user may see.
/// </summary>
public interface IEditionTrends
{
    /// <summary>
    /// One row per edition that is not a draft, the 10 most recent, oldest first, counting the entries of
    /// the orders of <paramref name="comparsaIds"/>, or of every comparsa when null.
    /// </summary>
    Task<IReadOnlyList<EditionTrendRow>> ListAsync(IReadOnlyCollection<Guid>? comparsaIds, CancellationToken cancellationToken);
}

/// <summary>One edition's counts (spec: Edition trends (UC-07)).</summary>
/// <param name="Year">The edition's year.</param>
/// <param name="Provisional">The edition is in progress, so its figures can still change.</param>
/// <param name="Active"><c>ACTIVE</c> entries.</param>
/// <param name="Reserve"><c>RESERVE</c> entries.</param>
/// <param name="PowderKg">Powder of every entry, in kilograms.</param>
/// <param name="CapsBoxes">Caps boxes of every entry.</param>
/// <param name="Owned"><c>ACTIVE</c> entries with an owned weapon.</param>
/// <param name="Rental"><c>ACTIVE</c> entries with a rented weapon.</param>
/// <param name="Loan"><c>ACTIVE</c> entries with a lent weapon.</param>
/// <param name="NoWeapon"><c>ACTIVE</c> entries without a weapon.</param>
/// <param name="RentalsByModel">Rented weapons of <c>ACTIVE</c> entries by catalogue model.</param>
/// <param name="FlaskRentals">Rented flasks, 1 and 2 kg.</param>
/// <param name="FirstYear">
/// <c>ACTIVE</c> entries of arquebusiers with no <c>ACTIVE</c> entry in an earlier edition, or null while no
/// earlier edition has orders. Entries of arquebusiers no longer in the registry are not counted.
/// </param>
/// <param name="ActiveByComparsa"><c>ACTIVE</c> entries per comparsa of the orders counted.</param>
/// <param name="ActiveArquebusierIds">
/// The arquebusiers of the <c>ACTIVE</c> entries still linked to the registry, for counts made on the
/// server (the gender); never sent to a client.
/// </param>
public sealed record EditionTrendRow(
    int Year,
    bool Provisional,
    int Active,
    int Reserve,
    int PowderKg,
    int CapsBoxes,
    int Owned,
    int Rental,
    int Loan,
    int NoWeapon,
    IReadOnlyDictionary<Guid, int> RentalsByModel,
    int FlaskRentals,
    int? FirstYear,
    IReadOnlyDictionary<Guid, int> ActiveByComparsa,
    IReadOnlyList<Guid> ActiveArquebusierIds);
