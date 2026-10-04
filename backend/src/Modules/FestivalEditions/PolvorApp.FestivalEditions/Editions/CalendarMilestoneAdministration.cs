using Microsoft.EntityFrameworkCore;
using PolvorApp.FestivalEditions.Persistence;
using PolvorApp.SharedKernel.Auditing;

namespace PolvorApp.FestivalEditions.Editions;

/// <summary>
/// A validated milestone: its date, its trimmed, single-line title and whether it is reminded by email.
/// A null <see cref="Notify"/> means "not given": off for a new milestone, unchanged for an edit
/// (add-notifications, design D10).
/// </summary>
internal sealed record CalendarMilestoneInput(DateOnly Date, string Title, bool? Notify);

/// <summary>
/// Calendar milestones of an edition (spec: Calendar milestones), managed by Admins in any status.
/// Each write locks the edition row, so the 50-milestone cap holds under concurrent adds.
/// </summary>
internal sealed class CalendarMilestoneAdministration(FestivalEditionsDbContext db, IAuditTrail trail, EditionWriteGuard guard, TimeProvider time)
{
    public const string EntityType = "CalendarMilestone";

    public Task<EditionWrite> AddAsync(Guid editionId, CalendarMilestoneInput input, CancellationToken cancellationToken) =>
        guard.RunAsync(nameof(AddAsync), editionId, async () =>
        {
            await using var transaction = await db.BeginWriteAsync(cancellationToken);
            if (!await db.LockEditionAsync(editionId, cancellationToken))
            {
                return EditionWrite.Failed(EditionOutcome.NotFound);
            }

            if (await db.Milestones.CountAsync(m => m.EditionId == editionId, cancellationToken) >= CalendarMilestone.MaxPerEdition)
            {
                return EditionWrite.Failed(EditionOutcome.TooManyMilestones);
            }

            var now = time.GetUtcNow();
            var milestone = new CalendarMilestone
            {
                Id = Guid.CreateVersion7(now),
                EditionId = editionId,
                Date = input.Date,
                Title = input.Title,
                Notify = input.Notify ?? false,
                CreatedAt = now,
            };
            db.Milestones.Add(milestone);
            Record(FestivalEditionsAuditActions.CalendarMilestoneAdded, milestone, new { editionId, milestone.Date, milestone.Title, milestone.Notify });
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return EditionWrite.Done(milestone);
        });

    /// <summary>Changes a milestone; the last save wins, and an unchanged save records nothing.</summary>
    public Task<EditionWrite> UpdateAsync(Guid editionId, Guid milestoneId, CalendarMilestoneInput input, CancellationToken cancellationToken) =>
        guard.RunAsync(nameof(UpdateAsync), editionId, async () =>
        {
            await using var transaction = await db.BeginWriteAsync(cancellationToken);
            if (await FindAsync(editionId, milestoneId, cancellationToken) is not { } milestone)
            {
                return await db.Editions.AnyAsync(e => e.Id == editionId, cancellationToken)
                    ? EditionWrite.Failed(EditionOutcome.MilestoneNotFound)
                    : EditionWrite.Failed(EditionOutcome.NotFound);
            }

            var notify = input.Notify ?? milestone.Notify;
            var previous = new CalendarMilestoneInput(milestone.Date, milestone.Title, milestone.Notify);
            var current = input with { Notify = notify };
            if (previous == current)
            {
                return EditionWrite.Done(milestone);
            }

            (milestone.Date, milestone.Title, milestone.Notify) = (current.Date, current.Title, notify);
            Record(FestivalEditionsAuditActions.CalendarMilestoneUpdated, milestone, new { editionId, previous, current });
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return EditionWrite.Done(milestone);
        });

    public Task<EditionWrite> RemoveAsync(Guid editionId, Guid milestoneId, CancellationToken cancellationToken) =>
        guard.RunAsync(nameof(RemoveAsync), editionId, async () =>
        {
            await using var transaction = await db.BeginWriteAsync(cancellationToken);
            if (await FindAsync(editionId, milestoneId, cancellationToken) is not { } milestone)
            {
                return await db.Editions.AnyAsync(e => e.Id == editionId, cancellationToken)
                    ? EditionWrite.Failed(EditionOutcome.MilestoneNotFound)
                    : EditionWrite.Failed(EditionOutcome.NotFound);
            }

            db.Milestones.Remove(milestone);
            Record(FestivalEditionsAuditActions.CalendarMilestoneRemoved, milestone, new { editionId, milestone.Date, milestone.Title, milestone.Notify });
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return EditionWrite.Done(milestone);
        });

    /// <summary>The milestone of that edition, after locking the edition; null when either is missing.</summary>
    private async Task<CalendarMilestone?> FindAsync(Guid editionId, Guid milestoneId, CancellationToken cancellationToken) =>
        await db.LockEditionAsync(editionId, cancellationToken)
            ? await db.Milestones.SingleOrDefaultAsync(m => m.Id == milestoneId && m.EditionId == editionId, cancellationToken)
            : null;

    private void Record(string action, CalendarMilestone milestone, object data) =>
        trail.Record(db, new AuditRecord(action, EntityType, milestone.Id.ToString(), data));
}
