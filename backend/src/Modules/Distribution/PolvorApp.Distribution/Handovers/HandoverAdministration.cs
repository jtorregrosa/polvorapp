using Microsoft.EntityFrameworkCore;
using PolvorApp.Distribution.Persistence;
using PolvorApp.FestivalEditions.Contracts;
using PolvorApp.SharedKernel.Auditing;

namespace PolvorApp.Distribution.Handovers;

/// <summary>
/// Undoes a recorded handover (spec: Powder handovers (UC-21)): with its current version, while the
/// edition is in progress, audited in the same transaction. Undoing frees the holder and the flask
/// number. Admin-only at the endpoint.
/// </summary>
internal sealed class HandoverAdministration(
    DistributionDbContext db,
    IEditionDirectory editions,
    IAuditTrail trail,
    DistributionWriteGuard guard)
{
    public Task<DistributionResult<Handover>> UndoAsync(Guid id, uint version, CancellationToken cancellationToken) =>
        guard.RunAsync(nameof(UndoAsync), id, async () =>
        {
            await using var transaction = await db.BeginWriteAsync(cancellationToken);
            var editionId = await db.Handovers.AsNoTracking().Where(h => h.Id == id)
                .Join(db.Days, h => h.DistributionId, d => d.Id, (h, d) => (Guid?)d.EditionId)
                .SingleOrDefaultAsync(cancellationToken);
            if (editionId is not { } edition)
            {
                return DistributionResult<Handover>.Failed(DistributionOutcome.NotFound);
            }

            if ((await db.ReadEditionForWriteAsync(editions, edition, cancellationToken))?.Status != EditionStatus.InProgress)
            {
                return DistributionResult<Handover>.Failed(DistributionOutcome.EditionNotInProgress);
            }

            var handover = await db.Handovers.SingleOrDefaultAsync(h => h.Id == id, cancellationToken);
            if (handover is null)
            {
                return DistributionResult<Handover>.Failed(DistributionOutcome.NotFound);
            }

            if (handover.Version != version)
            {
                return DistributionResult<Handover>.Failed(DistributionOutcome.Modified);
            }

            db.Handovers.Remove(handover);
            trail.Record(db, new AuditRecord(DistributionAuditActions.HandoverUndone, HandoverSync.EntityType, handover.Id.ToString(), HandoverSync.AuditData(handover)));
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Undone by another request after it was read.
                db.ChangeTracker.Clear();
                guard.LostRace(id, "concurrency");
                return DistributionResult<Handover>.Failed(DistributionOutcome.Modified);
            }

            await transaction.CommitAsync(cancellationToken);
            return DistributionResult<Handover>.Done(handover);
        });
}
