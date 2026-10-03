using System.Data.Common;

namespace PolvorApp.FestivalEditions.Contracts;

/// <summary>What other modules need to know about an edition (change add-festival-editions, design D7).</summary>
/// <param name="Id">Edition identifier.</param>
/// <param name="Year">Edition year.</param>
/// <param name="Status">Lifecycle status.</param>
/// <param name="OrdersOpen">
/// True only for the edition in progress while its orders are open: the single definition of BR-10's
/// window rule. FiringChiefs edit orders only while it is true; Admins always may.
/// </param>
/// <param name="FestivalStartsOn">First festival day: the reference date of the entries' age warning (BR-04; add-comparsa-orders, design D7).</param>
/// <param name="FestivalEndsOn">Last festival day: the license of an entry must be valid through it (BR-04).</param>
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

    /// <summary>
    /// The edition read <c>FOR SHARE</c> on the caller's own transaction (add-comparsa-orders, design
    /// D4): opening or closing the orders, a status move or a deletion of the edition waits until that
    /// transaction ends, so a write checked against <see cref="EditionSnapshot.OrdersOpen"/> commits
    /// before the orders close or not at all. Never commits, closes or disposes the connection. Null
    /// when no edition has that id. The caller's transaction must have its <c>lock_timeout</c> set, so
    /// the wait is bounded, and no reader may be open on its connection.
    /// </summary>
    Task<EditionSnapshot?> ReadForOrderWriteAsync(Guid editionId, DbTransaction transaction, CancellationToken cancellationToken);
}
