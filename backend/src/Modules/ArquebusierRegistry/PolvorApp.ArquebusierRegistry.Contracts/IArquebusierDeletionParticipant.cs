using System.Data.Common;

namespace PolvorApp.ArquebusierRegistry.Contracts;

/// <summary>
/// Implemented by modules that must act when an arquebusier is deleted, inside the same transaction
/// (add-comparsa-orders, design D3): comparsa orders remove the arquebusier's entry of the edition in
/// progress while its orders are open. The registry calls every implementation after locking the
/// arquebusier <c>FOR UPDATE</c> and before removing the row.
/// </summary>
/// <remarks>
/// An implementation runs its own SQL on <c>transaction.Connection</c>, which already runs the
/// registry's transaction. It never commits, closes or disposes the connection. A failure aborts the
/// whole deletion; a lock timeout or deadlock becomes the registry's retryable <c>registry.busy</c>.
/// With several participants, their registration order is the order in which they take their locks.
/// </remarks>
public interface IArquebusierDeletionParticipant
{
    /// <summary>Acts on the deletion of <paramref name="arquebusierId"/>, whose owned weapons are <paramref name="ownedWeaponIds"/>.</summary>
    Task<ArquebusierDeletionEffect> OnDeletingAsync(
        Guid arquebusierId, IReadOnlyCollection<Guid> ownedWeaponIds, DbTransaction transaction, CancellationToken cancellationToken);
}

/// <summary>
/// What a participant changed, recorded by ids only in the registry's <c>ArquebusierDeleted</c> audit
/// entry (no personal data).
/// </summary>
/// <param name="RemovedEntryIds">Edition entries removed with the arquebusier.</param>
/// <param name="AffectedOrderIds">The orders those entries belonged to.</param>
public sealed record ArquebusierDeletionEffect(IReadOnlyList<Guid> RemovedEntryIds, IReadOnlyList<Guid> AffectedOrderIds)
{
    /// <summary>Nothing changed.</summary>
    public static readonly ArquebusierDeletionEffect None = new([], []);
}
