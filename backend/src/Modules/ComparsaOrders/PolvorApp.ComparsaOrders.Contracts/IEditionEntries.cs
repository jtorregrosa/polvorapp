using System.Data.Common;
namespace PolvorApp.ComparsaOrders.Contracts;

/// <summary>
/// Edition entries as other modules read them, e.g. to register pickup proxies
/// (add-distribution-planning, design D3). It applies no comparsa scope and no edition or order-status
/// rule: callers enforce BR-12. Pass <see cref="ListAsync"/> only a comparsa the user may see; for
/// <see cref="FindManyAsync"/>, which takes bare entry ids, check each returned
/// <see cref="EditionEntryFacts.ComparsaId"/> and <see cref="EditionEntryFacts.EditionId"/> against the
/// user's access and treat a mismatch as not found. Entries of orders in any status are returned.
/// Server-side only; the records hold personal data and never print it.
/// </summary>
public interface IEditionEntries
{
    /// <summary>The most ids <see cref="FindManyAsync"/> takes at once: an edition has under a thousand entries.</summary>
    const int MaxIds = 1000;

    /// <summary>The entries of the comparsa's order in the edition, in no particular order; empty when it is not prepared.</summary>
    Task<IReadOnlyList<EditionEntryFacts>> ListAsync(Guid editionId, Guid comparsaId, CancellationToken cancellationToken);

    /// <summary>The entries with these ids, in no particular order; unknown ids are left out. At most <see cref="MaxIds"/> ids.</summary>
    Task<IReadOnlyList<EditionEntryFacts>> FindManyAsync(IReadOnlyCollection<Guid> entryIds, CancellationToken cancellationToken);

    /// <summary>The status of each comparsa's order in the edition, by comparsa; a comparsa without an order is absent (not prepared).</summary>
    Task<IReadOnlyDictionary<Guid, OrderStatus>> ListOrderStatusesAsync(Guid editionId, CancellationToken cancellationToken);

    /// <summary>
    /// Each prepared order of the edition with its comparsa and status, in no particular order; a comparsa
    /// without an order is absent (not prepared). For links and reminders (add-notifications, design D9).
    /// </summary>
    Task<IReadOnlyList<OrderSummary>> ListOrdersAsync(Guid editionId, CancellationToken cancellationToken);

    /// <summary>
    /// The ids of a person's entries in every edition, found by the copy's DNI/NIE or through the
    /// registered arquebusier with it, e.g. for a GDPR request (add-audit-privacy, design D5).
    /// </summary>
    Task<IReadOnlyList<Guid>> ListEntryIdsOfPersonAsync(string normalisedNationalId, CancellationToken cancellationToken);

    /// <summary>
    /// Locks the entries <c>FOR SHARE</c> on the caller's <paramref name="transaction"/> and tells whether
    /// any of them was erased on a GDPR request: an erasure of them waits until the caller commits, and
    /// one committed before is seen (add-audit-privacy, design D7). Unknown ids are ignored.
    /// </summary>
    Task<bool> AnyErasedForWriteAsync(IReadOnlyCollection<Guid> entryIds, DbTransaction transaction, CancellationToken cancellationToken);
}

/// <summary>A comparsa's order in an edition, without its entries.</summary>
/// <param name="OrderId">Order identifier.</param>
/// <param name="ComparsaId">Its comparsa.</param>
/// <param name="Status">Its status.</param>
public sealed record OrderSummary(Guid OrderId, Guid ComparsaId, OrderStatus Status);

/// <summary>An entry with its order and what it collects on distribution day.</summary>
/// <param name="EntryId">Entry identifier.</param>
/// <param name="OrderId">Its order.</param>
/// <param name="EditionId">The order's edition.</param>
/// <param name="ComparsaId">The order's comparsa: an entry never changes order (BR-13).</param>
/// <param name="OrderStatus">The order's status.</param>
/// <param name="ArquebusierId">The arquebusier, or null once deleted from the registry.</param>
/// <param name="IsActive">Whether the entry's status for the edition is <c>ACTIVE</c> (otherwise <c>RESERVE</c>).</param>
/// <param name="PowderKg">Powder in kilograms.</param>
/// <param name="WeaponSource">Where the weapon comes from.</param>
/// <param name="RentalWeaponModelId">The rented model, for <see cref="WeaponSource.Rental"/>.</param>
/// <param name="Flask">The flask.</param>
/// <param name="Copy">The entry's copy of the arquebusier's identity (spec: Entry history (BR-14)).</param>
/// <param name="Erased">True once the person was erased on a GDPR request: never a pickup holder or proxy.</param>
public sealed record EditionEntryFacts(
    Guid EntryId,
    Guid OrderId,
    Guid EditionId,
    Guid ComparsaId,
    OrderStatus OrderStatus,
    Guid? ArquebusierId,
    bool IsActive,
    int PowderKg,
    WeaponSource WeaponSource,
    Guid? RentalWeaponModelId,
    FlaskOption Flask,
    ExportedPerson Copy,
    bool Erased = false)
{
    /// <summary>The type name only: the copy is personal data.</summary>
    public override string ToString() => nameof(EditionEntryFacts);
}
