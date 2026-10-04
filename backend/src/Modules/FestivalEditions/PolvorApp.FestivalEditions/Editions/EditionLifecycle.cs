using Microsoft.EntityFrameworkCore;
using PolvorApp.FestivalEditions.Contracts;
using PolvorApp.FestivalEditions.Persistence;
using PolvorApp.Notifications.Contracts;
using PolvorApp.SharedKernel.Auditing;

namespace PolvorApp.FestivalEditions.Editions;

/// <summary>
/// The edition lifecycle and its orders (specs: Edition lifecycle (UC-11), Opening and closing
/// orders (UC-11, BR-10); design D3). Each change locks the edition row, compares its version and is
/// audited in the same transaction; setting the current value records nothing.
/// </summary>
internal sealed class EditionLifecycle(
    FestivalEditionsDbContext db, IAuditTrail trail, INotificationOutbox notifications, EditionWriteGuard guard, TimeProvider time)
{
    /// <summary>
    /// Moves the edition one step. The unique index keeps a single edition in progress under
    /// concurrency; the query before it only names the edition in progress for the message.
    /// </summary>
    public Task<EditionWrite> ChangeStatusAsync(Guid id, EditionStatus to, uint version, CancellationToken cancellationToken) =>
        guard.RunAsync(nameof(ChangeStatusAsync), id, async () =>
        {
            await using var transaction = await db.BeginWriteAsync(cancellationToken);
            if (!await db.LockEditionAsync(id, cancellationToken))
            {
                return EditionWrite.Failed(EditionOutcome.NotFound);
            }

            var edition = await db.Editions.SingleAsync(e => e.Id == id, cancellationToken);
            if (edition.Version != version)
            {
                return EditionWrite.Failed(EditionOutcome.Modified);
            }

            var (outcome, missing) = EditionStatusMoves.Check(edition, to);
            if (outcome != EditionOutcome.Done)
            {
                return EditionWrite.Failed(outcome, outcome == EditionOutcome.Incomplete ? new Dictionary<string, object?> { ["missing"] = missing } : null);
            }

            if (to == EditionStatus.InProgress && await InProgressYearAsync(cancellationToken) is { } year)
            {
                return AnotherInProgress(year);
            }

            var previous = edition.Status;
            edition.Status = to;
            edition.StatusChangedAt = time.GetUtcNow();
            Record(FestivalEditionsAuditActions.EditionStatusChanged, edition, new { previous, current = to });
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (EditionProblems.IsUniqueViolation(exception, FestivalEditionsDbContext.InProgressIndex))
            {
                // Another edition started meanwhile. The transaction is aborted: roll it back before
                // naming the winner; it may have been closed again already, so the year can be null.
                db.ChangeTracker.Clear();
                await transaction.RollbackAsync(cancellationToken);
                guard.LostRace(id, FestivalEditionsDbContext.InProgressIndex);
                return AnotherInProgress(await InProgressYearAsync(cancellationToken));
            }

            await transaction.CommitAsync(cancellationToken);
            return EditionWrite.Done(edition);
        });

    /// <summary>Opens or closes the orders of the edition in progress.</summary>
    public Task<EditionWrite> SetOrdersAsync(Guid id, bool open, uint version, CancellationToken cancellationToken) =>
        guard.RunAsync(nameof(SetOrdersAsync), id, async () =>
        {
            await using var transaction = await db.BeginWriteAsync(cancellationToken);
            if (!await db.LockEditionAsync(id, cancellationToken))
            {
                return EditionWrite.Failed(EditionOutcome.NotFound);
            }

            var edition = await db.Editions.SingleAsync(e => e.Id == id, cancellationToken);
            if (edition.Version != version)
            {
                return EditionWrite.Failed(EditionOutcome.Modified);
            }

            if (edition.Status != EditionStatus.InProgress)
            {
                return EditionWrite.Failed(EditionOutcome.NotInProgress);
            }

            if (edition.OrdersOpen == open)
            {
                return EditionWrite.Done(edition);
            }

            edition.OrdersOpen = open;
            Record(open ? FestivalEditionsAuditActions.EditionOrdersOpened : FestivalEditionsAuditActions.EditionOrdersClosed, edition);
            notifications.Record(db, open ? NotificationEvent.OrdersOpened(edition.Id) : NotificationEvent.OrdersClosed(edition.Id));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return EditionWrite.Done(edition);
        });

    /// <summary>The problem names the edition in progress; null when it could not be determined.</summary>
    private static EditionWrite AnotherInProgress(int? year) =>
        EditionWrite.Failed(EditionOutcome.AnotherInProgress, new Dictionary<string, object?> { ["inProgressYear"] = year });

    private Task<int?> InProgressYearAsync(CancellationToken cancellationToken) =>
        db.Editions.AsNoTracking()
            .Where(e => e.Status == EditionStatus.InProgress)
            .Select(e => (int?)e.Year)
            .FirstOrDefaultAsync(cancellationToken);

    private void Record(string action, FestivalEdition edition, object? data = null) =>
        trail.Record(db, new AuditRecord(action, EditionAdministration.EntityType, edition.Id.ToString(), data));
}
