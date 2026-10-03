using System.Collections.Frozen;

namespace PolvorApp.ComparsaOrders.Contracts;

/// <summary>
/// What the comparsa orders tell other modules about an arquebusier's participation (add-comparsa-orders,
/// design D5): the "first year" flag (UC-07) and what a deletion does to their entries. Server-side only;
/// it applies no comparsa scope, so callers pass only arquebusiers the user may already see.
/// </summary>
public interface IParticipationHistory
{
    /// <summary>
    /// Which of <paramref name="arquebusierIds"/> are in their first year in the edition of
    /// <paramref name="editionYear"/>: no <c>ACTIVE</c> entry in an edition with an earlier year. Known only
    /// when an earlier edition has at least one order (maintainer decision); otherwise
    /// <see cref="FirstYearResult.Unknown"/>.
    /// </summary>
    Task<FirstYearResult> FirstYearAsync(int editionYear, IReadOnlyCollection<Guid> arquebusierIds, CancellationToken cancellationToken);

    /// <summary>
    /// What deleting the arquebusier would do to the orders, for the delete confirmation: their entry in
    /// the edition in progress, how many of <paramref name="ownedWeaponIds"/> (their owned weapons) are lent
    /// in it, and whether entries of other editions stay as history.
    /// </summary>
    Task<DeletionImpact> GetDeletionImpactAsync(Guid arquebusierId, IReadOnlyCollection<Guid> ownedWeaponIds, CancellationToken cancellationToken);
}

/// <summary>The first-year flag of several arquebusiers in one edition.</summary>
/// <param name="Known">False while no earlier edition has orders: the flag is then not shown or counted.</param>
/// <param name="FirstYearIds">The arquebusiers in their first year; empty when not known.</param>
public sealed record FirstYearResult(bool Known, IReadOnlySet<Guid> FirstYearIds)
{
    /// <summary>No history yet.</summary>
    public static readonly FirstYearResult Unknown = new(false, FrozenSet<Guid>.Empty);

    /// <summary>The flag of one arquebusier: null when not known.</summary>
    public bool? Of(Guid arquebusierId) => Known ? FirstYearIds.Contains(arquebusierId) : null;
}

/// <summary>What deleting an arquebusier does to the orders (spec: Deleting an arquebusier).</summary>
/// <param name="CurrentEntry">Their entry in the edition in progress, or null.</param>
/// <param name="LentWeaponsInCurrentEdition">How many of their owned weapons are lent in the edition in progress; those loans will show the weapon as removed.</param>
/// <param name="HasPastEntries">Whether they have entries in other editions, kept as history.</param>
public sealed record DeletionImpact(CurrentEntryImpact? CurrentEntry, int LentWeaponsInCurrentEdition, bool HasPastEntries)
{
    /// <summary>No entry and no loan.</summary>
    public static readonly DeletionImpact None = new(null, 0, false);
}

/// <summary>The arquebusier's entry in the edition in progress.</summary>
/// <param name="EditionYear">The edition's year.</param>
/// <param name="ComparsaId">The comparsa whose order holds the entry.</param>
/// <param name="OrderStatus">The order's status.</param>
/// <param name="WillBeRemoved">
/// Whether the deletion removes it: the orders are open now. The deletion rereads it under lock, so an
/// order closing in between keeps the entry, which is the safe side.
/// </param>
public sealed record CurrentEntryImpact(int EditionYear, Guid ComparsaId, OrderStatus OrderStatus, bool WillBeRemoved);
