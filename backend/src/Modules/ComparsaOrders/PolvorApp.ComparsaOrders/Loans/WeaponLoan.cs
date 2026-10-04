using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.ComparsaOrders.Entries;

namespace PolvorApp.ComparsaOrders.Loans;

/// <summary>
/// The weapon lent to an entry with weapon source <c>LOAN</c> (spec: Weapon loans (UC-13, BR-09)).
/// The lender and the weapon are stored in the same columns for both kinds: typed for an external
/// owner, copied from the registry for a registered one (design D3, D9).
/// </summary>
internal sealed class WeaponLoan
{
    public required Guid Id { get; init; }

    public required Guid EntryId { get; init; }

    public required LenderKind LenderKind { get; set; }

    /// <summary>A registered owner's weapon; null for an external owner, or once it leaves the registry.</summary>
    public Guid? LenderOwnedWeaponId { get; set; }

    // The lender and the weapon. Nullable only so a GDPR erasure (#15) can blank them.
    public string? LenderFirstName { get; set; }

    public string? LenderLastName { get; set; }

    public string? LenderNationalId { get; set; }

    /// <summary>The registered owner's comparsa; null for an external owner.</summary>
    public Guid? LenderComparsaId { get; set; }

    public Guid? WeaponModelId { get; set; }

    public string? WeaponNumber { get; set; }

    public string? OwnershipGuideNumber { get; set; }

    public required DateTimeOffset CopiedAt { get; set; }

    /// <summary>When a GDPR erasure of the lender blanked their identity and the weapon's number and guide.</summary>
    public DateTimeOffset? ErasedAt { get; set; }
}
