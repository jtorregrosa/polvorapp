namespace PolvorApp.ComparsaOrders.Contracts;

/// <summary>
/// The orders as the exports read them (change add-exports, design D2). It applies no comparsa scope:
/// callers enforce BR-12. Server-side only; the records hold personal data and never print it.
/// </summary>
public interface IOrderExports
{
    /// <summary>The <c>VALIDATED</c> orders of the edition, for the recipient exports.</summary>
    /// <remarks>
    /// Every comparsa: for Federation exports only (Admins). The caller records the export with
    /// <c>IAuditLog</c> before it returns the file.
    /// </remarks>
    Task<IReadOnlyList<ExportedOrder>> ListValidatedAsync(Guid editionId, CancellationToken cancellationToken);

    /// <summary>The comparsa's order in the edition, in any status, or null when not prepared.</summary>
    /// <remarks>
    /// Call it only with a comparsa the user may see (a FiringChief: their own; BR-12). An order that
    /// is not <c>VALIDATED</c> may still change: whatever is built from it is a draft. The caller
    /// records the export with <c>IAuditLog</c> before it returns the file.
    /// </remarks>
    Task<ExportedOrder?> FindAsync(Guid editionId, Guid comparsaId, CancellationToken cancellationToken);
}

/// <summary>An order with its entries, in no particular order.</summary>
public sealed record ExportedOrder(Guid OrderId, Guid ComparsaId, OrderStatus Status, IReadOnlyList<ExportedEntry> Entries)
{
    /// <summary>The type name only: the entries are personal data.</summary>
    public override string ToString() => nameof(ExportedOrder);
}

/// <summary>
/// One entry: its values, the live links (null once the arquebusier or weapon left the registry) and
/// the history copy the entry keeps (spec: Entry history (BR-14)).
/// </summary>
/// <param name="EntryId">Entry identifier.</param>
/// <param name="ArquebusierId">The arquebusier, or null once deleted from the registry.</param>
/// <param name="IsActive">Whether the entry's status for the edition is <c>ACTIVE</c> (otherwise <c>RESERVE</c>).</param>
/// <param name="PowderKg">Powder in kilograms.</param>
/// <param name="CapsBoxes">Caps boxes.</param>
/// <param name="CapsType">Their type, when there are boxes.</param>
/// <param name="WeaponSource">Where the weapon comes from.</param>
/// <param name="OwnedWeaponId">The owned weapon, while it is in the registry.</param>
/// <param name="RentalWeaponModelId">The rented model, for <see cref="WeaponSource.Rental"/>.</param>
/// <param name="Flask">The flask.</param>
/// <param name="Person">The copy of the arquebusier's identity.</param>
/// <param name="OwnedWeapon">The copy of the owned weapon, for <see cref="WeaponSource.Owned"/>.</param>
/// <param name="Loan">The loan, for <see cref="WeaponSource.Loan"/>.</param>
/// <param name="Erased">
/// True once the person was erased on a GDPR request (add-audit-privacy): lists of people leave the
/// entry out, totals still count it.
/// </param>
public sealed record ExportedEntry(
    Guid EntryId,
    Guid? ArquebusierId,
    bool IsActive,
    int PowderKg,
    int CapsBoxes,
    CapsType? CapsType,
    WeaponSource WeaponSource,
    Guid? OwnedWeaponId,
    Guid? RentalWeaponModelId,
    FlaskOption Flask,
    ExportedPerson Person,
    ExportedWeapon? OwnedWeapon,
    ExportedLoan? Loan,
    bool Erased = false)
{
    /// <summary>The type name only.</summary>
    public override string ToString() => nameof(ExportedEntry);
}

/// <summary>A person as an entry or a loan copied them; the fields are null only after a GDPR erasure.</summary>
public sealed record ExportedPerson(string? FirstName, string? LastName, string? NationalId, int? FederationId)
{
    /// <summary>The type name only.</summary>
    public override string ToString() => nameof(ExportedPerson);
}

/// <summary>A weapon as an entry or a loan copied it.</summary>
public sealed record ExportedWeapon(Guid? WeaponModelId, string? WeaponNumber, string? OwnershipGuideNumber)
{
    /// <summary>The type name only: the ownership guide identifies the owner.</summary>
    public override string ToString() => nameof(ExportedWeapon);
}

/// <summary>
/// A weapon loan: the lender, registered or external, and the lent weapon, as copied. The lender's
/// <see cref="ExportedPerson.FederationId"/> is always null: loans do not copy it.
/// </summary>
public sealed record ExportedLoan(LenderKind LenderKind, ExportedPerson Lender, Guid? LenderComparsaId, ExportedWeapon Weapon)
{
    /// <summary>The type name only.</summary>
    public override string ToString() => nameof(ExportedLoan);
}
