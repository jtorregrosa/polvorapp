using PolvorApp.ComparsaOrders.Loans;

namespace PolvorApp.ComparsaOrders.Entries;

/// <summary>
/// The data problems of an entry, on the current data (specs: Submitting an order (UC-14), Reviewing
/// orders (UC-15), Entries after registry changes (BR-13, BR-14); design D8). Each one blocks the
/// submission and the validation of its order until the entry is changed. The order response, the
/// submission and the validation all use these rules. An <c>ACTIVE</c> entry without powder or without
/// a weapon is valid: some arquebusiers only fire, others only carry powder.
/// </summary>
internal static class EntryIssues
{
    /// <summary>The entry's owned weapon left the registry while its arquebusier is still in it.</summary>
    public const string OwnedWeaponMissing = "ownedWeaponMissing";

    /// <summary>A registered lender's weapon left the registry while the borrower is still in it.</summary>
    public const string LoanWeaponMissing = "loanWeaponMissing";

    /// <summary>The rental model is no longer offered in the edition (BR-07).</summary>
    public const string RentalModelNotOffered = "rentalModelNotOffered";

    /// <param name="entry">The entry.</param>
    /// <param name="loan">Its loan, for a <c>LOAN</c> entry.</param>
    /// <param name="offeredModelIds">The models offered for rental in the edition now.</param>
    public static IReadOnlyList<string> Of(EditionEntry entry, WeaponLoan? loan, IReadOnlySet<Guid> offeredModelIds)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(offeredModelIds);

        // History of an arquebusier who left the registry keeps its weapon and loan copies and does not
        // block: their entry stays only once the orders are closed, and must not stop an Admin's validation.
        var inRegistry = entry.ArquebusierId is not null;
        var issues = new List<string>(capacity: 1);
        switch (entry.WeaponSource)
        {
            case WeaponSource.Owned when inRegistry && entry.OwnedWeaponId is null:
                issues.Add(OwnedWeaponMissing);
                break;
            case WeaponSource.Loan when inRegistry && loan is { LenderKind: LenderKind.Arquebusier, LenderOwnedWeaponId: null }:
                issues.Add(LoanWeaponMissing);
                break;
            case WeaponSource.Rental when entry.RentalWeaponModelId is not { } model || !offeredModelIds.Contains(model):
                issues.Add(RentalModelNotOffered);
                break;
        }

        return issues;
    }
}
