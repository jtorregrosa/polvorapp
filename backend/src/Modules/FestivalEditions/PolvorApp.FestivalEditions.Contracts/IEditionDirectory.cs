namespace PolvorApp.FestivalEditions.Contracts;

/// <summary>What other modules need to know about an edition (change add-festival-editions, design D7).</summary>
/// <param name="Id">Edition identifier.</param>
/// <param name="Year">Edition year.</param>
/// <param name="Status">Lifecycle status.</param>
/// <param name="OrdersOpen">
/// True only for the edition in progress while its orders are open: the single definition of BR-10's
/// window rule. FiringChiefs edit orders only while it is true; Admins always may.
/// </param>
/// <param name="FestivalStartsOn">First festival day: the reference date of the compliance warnings of its entries (BR-04).</param>
/// <param name="FestivalEndsOn">Last festival day.</param>
/// <param name="OfferedWeaponModelIds">Models offered for rental: in the edition's set, active and rentable (BR-07).</param>
public sealed record EditionSnapshot(
    Guid Id,
    int Year,
    EditionStatus Status,
    bool OrdersOpen,
    DateOnly FestivalStartsOn,
    DateOnly FestivalEndsOn,
    IReadOnlyList<Guid> OfferedWeaponModelIds);

/// <summary>
/// Read-only lookup of festival editions for other modules, e.g. comparsa orders (#10). It applies no
/// visibility rule: callers acting for a FiringChief must not reveal a <see cref="EditionStatus.Draft"/>.
/// </summary>
public interface IEditionDirectory
{
    /// <summary>The edition in progress, or null when there is none.</summary>
    Task<EditionSnapshot?> GetCurrentAsync(CancellationToken cancellationToken);

    /// <summary>The edition, or null when none has that id.</summary>
    Task<EditionSnapshot?> FindAsync(Guid editionId, CancellationToken cancellationToken);
}
