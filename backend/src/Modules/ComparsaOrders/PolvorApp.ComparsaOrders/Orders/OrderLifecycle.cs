using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.ComparsaOrders.Entries;
using PolvorApp.ComparsaOrders.Loans;
using PolvorApp.ComparsaOrders.Persistence;
using PolvorApp.ComplianceInsights.Contracts;
using PolvorApp.FestivalEditions.Contracts;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.Notifications.Contracts;
using PolvorApp.SharedKernel.Auditing;

namespace PolvorApp.ComparsaOrders.Orders;

/// <summary>
/// Submission and review of comparsa orders (specs: Submitting an order (UC-14), Reviewing orders
/// (UC-15); design D8). Each move reads the edition <c>FOR SHARE</c> (BR-10), locks the order within the
/// caller's scope, checks its version and the data rules of every entry, refreshes the history copies
/// (design D3), and is audited in the same transaction.
/// </summary>
internal sealed class OrderLifecycle(
    ComparsaOrdersDbContext db,
    OrderGate gate,
    OrderWriteGuard guard,
    IComparsaScope scope,
    ICurrentUser currentUser,
    IArquebusierRoster roster,
    IComplianceRules rules,
    LoanWriter loans,
    IAuditTrail trail,
    INotificationOutbox notifications,
    TimeProvider time)
{
    /// <summary>Submits a draft or returned order: a FiringChief with the attestation while the orders are open, an Admin on the comparsa's behalf at any time.</summary>
    public Task<OrderResult<ComparsaOrder>> SubmitAsync(Guid orderId, uint version, CancellationToken cancellationToken) =>
        MoveAsync(OrderMove.Submit, orderId, version, reason: null, cancellationToken);

    /// <summary>Validates an order that is submitted, or that the comparsa never submitted (Admins).</summary>
    public Task<OrderResult<ComparsaOrder>> ValidateAsync(Guid orderId, uint version, CancellationToken cancellationToken) =>
        MoveAsync(OrderMove.Validate, orderId, version, reason: null, cancellationToken);

    /// <summary>Returns a submitted or validated order with a reason (Admins).</summary>
    public Task<OrderResult<ComparsaOrder>> ReturnAsync(Guid orderId, uint version, string reason, CancellationToken cancellationToken) =>
        MoveAsync(OrderMove.Return, orderId, version, reason, cancellationToken);

    private Task<OrderResult<ComparsaOrder>> MoveAsync(OrderMove move, Guid orderId, uint version, string? reason, CancellationToken cancellationToken) =>
        guard.RunAsync(move.ToString(), orderId, async () =>
        {
            var access = await scope.GetAccessAsync(cancellationToken);
            var target = await db.Orders.AsNoTracking()
                .Where(o => o.Id == orderId)
                .Select(o => new { o.EditionId, o.ComparsaId })
                .SingleOrDefaultAsync(cancellationToken);
            if (target is null || !access.CanAccess(target.ComparsaId))
            {
                return OrderResult<ComparsaOrder>.Failed(OrderOutcome.NotFound);
            }

            await using var transaction = await db.BeginWriteAsync(cancellationToken);
            var (gateOutcome, edition) = await gate.CheckEditAsync(target.EditionId, transaction.GetDbTransaction(), cancellationToken);
            if (edition is null)
            {
                return OrderResult<ComparsaOrder>.Failed(gateOutcome);
            }

            if (!await db.LockOrderAsync(orderId, access, cancellationToken))
            {
                return OrderResult<ComparsaOrder>.Failed(OrderOutcome.NotFound);
            }

            var order = await db.Orders.SingleAsync(o => o.Id == orderId, cancellationToken);
            var refusal = order.Version != version ? OrderOutcome.Modified
                : !OrderStatusMoves.IsAllowed(move, order.Status, currentUser.IsAdmin) ? OrderOutcome.InvalidTransition
                : OrderOutcome.Done;
            if (refusal != OrderOutcome.Done)
            {
                return OrderResult<ComparsaOrder>.Failed(refusal);
            }

            var checkedEntries = move == OrderMove.Return ? null : await CheckEntriesAsync(order, edition, cancellationToken);
            switch (checkedEntries)
            {
                case { Stale: true }:
                    return OrderResult<ComparsaOrder>.Failed(OrderOutcome.Modified);
                case { Invalid.Count: > 0 }:
                    return OrderResult<ComparsaOrder>.Failed(OrderOutcome.EntriesInvalid, new Dictionary<string, object?> { ["entries"] = checkedEntries.Invalid });
            }

            Apply(order, move, reason, checkedEntries?.WithWarnings ?? 0);
            var saved = await db.SaveAsync(guard, orderId, onEntryIndex: OrderOutcome.Busy, cancellationToken, onConcurrency: OrderOutcome.Modified);
            if (saved != OrderOutcome.Done)
            {
                return OrderResult<ComparsaOrder>.Failed(saved);
            }

            await transaction.CommitAsync(cancellationToken);
            return OrderResult<ComparsaOrder>.Done(order);
        });

    /// <summary>
    /// The entries with data problems (design D8), and the number of <c>ACTIVE</c> entries with
    /// compliance warnings. On the way, every entry and registered loan takes a fresh history copy, with
    /// two registry reads for the entries and two for the loans. <c>Stale</c> when an owned weapon left the
    /// registry after the entries were read: weapon removals do not take the order lock.
    /// </summary>
    private async Task<CheckedEntries> CheckEntriesAsync(ComparsaOrder order, EditionSnapshot edition, CancellationToken cancellationToken)
    {
        var entries = await db.Entries.Where(e => e.OrderId == order.Id).OrderBy(e => e.Id).ToListAsync(cancellationToken);
        var entryIds = entries.Select(e => e.Id).ToArray();
        var entryLoans = await db.Loans.Where(l => entryIds.Contains(l.EntryId)).ToDictionaryAsync(l => l.EntryId, cancellationToken);
        Guid[] arquebusierIds = [.. entries.Select(e => e.ArquebusierId).OfType<Guid>()];
        var live = arquebusierIds.Length == 0 ? [] : (await roster.FindManyAsync(arquebusierIds, cancellationToken)).ToDictionary(a => a.Id);
        var offered = edition.OfferedWeaponModelIds.ToHashSet();
        var now = time.GetUtcNow();
        var invalid = new List<InvalidEntry>();
        var withWarnings = 0;
        foreach (var entry in entries)
        {
            // A deletion takes the order lock before it unlinks the entries (design D3), so a link always has its arquebusier here.
            var arquebusier = entry.ArquebusierId is not { } id ? null
                : live.GetValueOrDefault(id) ?? throw new InvalidOperationException($"Entry {entry.Id} links to an arquebusier the registry does not have.");
            var loan = entryLoans.GetValueOrDefault(entry.Id);
            var issues = EntryIssues.Of(entry, loan, offered);
            if (issues.Count > 0)
            {
                invalid.Add(new InvalidEntry(entry.Id, issues));
            }

            if (arquebusier is not null)
            {
                if (!EntryCopies.TryRefresh(entry, arquebusier, now))
                {
                    return new CheckedEntries([], 0, Stale: true);
                }

                if (edition.Status == EditionStatus.InProgress && entry.Status == ArquebusierStatus.Active
                    && rules.EvaluateForFestival(EntryCopies.FactsOf(arquebusier), edition.FestivalStartsOn, edition.FestivalEndsOn).Count > 0)
                {
                    withWarnings++;
                }
            }
        }

        await loans.RefreshManyAsync(entryLoans.Values, now, cancellationToken);
        return new CheckedEntries(invalid, withWarnings);
    }

    private void Apply(ComparsaOrder order, OrderMove move, string? reason, int entriesWithWarnings)
    {
        var userId = currentUser.UserId ?? throw new InvalidOperationException("A write needs a signed-in user.");
        var now = time.GetUtcNow();
        var previous = order.Status;
        order.Status = OrderStatusMoves.Target(move);
        order.UpdatedAt = now;
        object data;
        switch (move)
        {
            case OrderMove.Submit:
                (order.SubmittedAt, order.SubmittedByUserId, order.ReturnReason) = (now, userId, null);
                (order.Attested, order.SubmittedByAdmin) = (!currentUser.IsAdmin, currentUser.IsAdmin);
                data = new { previous, current = order.Status, attested = order.Attested, byAdmin = order.SubmittedByAdmin, entriesWithWarnings };
                break;
            case OrderMove.Validate:
                (order.ReviewedAt, order.ReviewedByUserId, order.ReturnReason) = (now, userId, null);
                data = new { previous, current = order.Status, entriesWithWarnings };
                break;
            default:
                (order.ReviewedAt, order.ReviewedByUserId, order.ReturnReason) = (now, userId, reason);

                // The reason stays on the order; the audit trail never copies free text.
                data = new { previous, current = order.Status };
                break;
        }

        trail.Record(db, new AuditRecord("ComparsaOrder" + Past(move), OrderAdministration.EntityType, order.Id.ToString(), data, ComparsaId: order.ComparsaId));
        if (Event(order, move, currentUser.IsAdmin) is { } notification)
        {
            notifications.Record(db, notification);
        }
    }

    /// <summary>
    /// What the move tells others (add-notifications, spec: Order status emails): a FiringChief's
    /// submission tells the Admins, a review tells the comparsa's FiringChiefs; an Admin's submission on
    /// the comparsa's behalf tells nobody.
    /// </summary>
    private static NotificationEvent? Event(ComparsaOrder order, OrderMove move, bool byAdmin) => move switch
    {
        OrderMove.Submit when byAdmin => null,
        OrderMove.Submit => NotificationEvent.OrderSubmitted(order.EditionId, order.ComparsaId, order.Id),
        OrderMove.Validate => NotificationEvent.OrderValidated(order.EditionId, order.ComparsaId, order.Id),
        _ => NotificationEvent.OrderReturned(order.EditionId, order.ComparsaId, order.Id),
    };

    private static string Past(OrderMove move) => move switch
    {
        OrderMove.Submit => "Submitted",
        OrderMove.Validate => "Validated",
        _ => "Returned",
    };

    /// <summary>An entry that blocks the move, with its reasons (<c>orders.entriesInvalid</c>).</summary>
    internal sealed record InvalidEntry(Guid EntryId, IReadOnlyList<string> Reasons);

    private sealed record CheckedEntries(List<InvalidEntry> Invalid, int WithWarnings, bool Stale = false);
}
