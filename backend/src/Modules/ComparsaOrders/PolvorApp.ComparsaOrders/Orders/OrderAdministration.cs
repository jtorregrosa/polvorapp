using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ComparsaOrders.Entries;
using PolvorApp.ComparsaOrders.Persistence;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.FestivalEditions.Contracts;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.SharedKernel.Auditing;

namespace PolvorApp.ComparsaOrders.Orders;

/// <summary>
/// Preparation of comparsa orders (specs: Comparsa orders (UC-12), Preparing an order (UC-12), Pre-fill
/// from the previous edition (UC-12, BR-11); design D8). The write runs through the write guard, reads
/// the edition <c>FOR SHARE</c> (BR-10), and is audited in the same transaction.
/// </summary>
internal sealed class OrderAdministration(
    ComparsaOrdersDbContext db,
    OrderGate gate,
    OrderWriteGuard guard,
    IComparsaScope scope,
    ICurrentUser currentUser,
    ICatalogDirectory catalog,
    IArquebusierRoster roster,
    EntryHistory history,
    IAuditTrail trail,
    TimeProvider time)
{
    public const string EntityType = "ComparsaOrder";

    /// <summary>Creates the order with one pre-filled entry per arquebusier of the comparsa who has none in the edition.</summary>
    public Task<OrderResult<ComparsaOrder>> PrepareAsync(Guid editionId, Guid comparsaId, CancellationToken cancellationToken) =>
        guard.RunAsync(nameof(PrepareAsync), null, async () =>
        {
            var userId = currentUser.UserId ?? throw new InvalidOperationException("A write needs a signed-in user.");
            if (!(await scope.GetAccessAsync(cancellationToken)).CanAccess(comparsaId))
            {
                return OrderResult<ComparsaOrder>.Failed(OrderOutcome.NotFound);
            }

            await using var transaction = await db.BeginWriteAsync(cancellationToken);
            var (outcome, edition) = await gate.CheckEditAsync(editionId, transaction.GetDbTransaction(), cancellationToken);
            if (edition is null)
            {
                return OrderResult<ComparsaOrder>.Failed(outcome);
            }

            var refusal = await CheckComparsaAsync(editionId, comparsaId, cancellationToken);
            if (refusal != OrderOutcome.Done)
            {
                return OrderResult<ComparsaOrder>.Failed(refusal);
            }

            var order = await AddOrderAsync(edition, comparsaId, userId, cancellationToken);
            var saved = await db.SaveAsync(guard, order.Id, onEntryIndex: OrderOutcome.Busy, cancellationToken);
            if (saved != OrderOutcome.Done)
            {
                return OrderResult<ComparsaOrder>.Failed(saved);
            }

            await transaction.CommitAsync(cancellationToken);
            return OrderResult<ComparsaOrder>.Done(order);
        });

    /// <summary>The comparsa must exist, be active and have no order in the edition yet.</summary>
    private async Task<OrderOutcome> CheckComparsaAsync(Guid editionId, Guid comparsaId, CancellationToken cancellationToken)
    {
        switch (await catalog.FindComparsaAsync(comparsaId, cancellationToken))
        {
            case null:
                return OrderOutcome.NotFound;
            case { Active: false }:
                return OrderOutcome.ComparsaInactive;
        }

        return await db.Orders.AnyAsync(o => o.EditionId == editionId && o.ComparsaId == comparsaId, cancellationToken)
            ? OrderOutcome.AlreadyPrepared
            : OrderOutcome.Done;
    }

    /// <summary>Adds the order, its pre-filled entries and the audit entry to the context.</summary>
    private async Task<ComparsaOrder> AddOrderAsync(EditionSnapshot edition, Guid comparsaId, Guid userId, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var order = new ComparsaOrder
        {
            Id = Guid.CreateVersion7(),
            EditionId = edition.Id,
            EditionYear = edition.Year,
            ComparsaId = comparsaId,
            PreparedAt = now,
            PreparedByUserId = userId,
            UpdatedAt = now,
        };
        var arquebusiers = await history.WithoutEntryAsync(edition.Id, await roster.ListByComparsaAsync(comparsaId, cancellationToken), cancellationToken);
        var previous = await history.PreviousEntriesAsync([.. arquebusiers.Select(a => a.Id)], edition.Year, cancellationToken);
        var offered = edition.OfferedWeaponModelIds.ToHashSet();
        var entries = arquebusiers.Select(a => EntryHistory.NewEntry(order, a, previous.GetValueOrDefault(a.Id), offered, now)).ToList();

        db.Orders.Add(order);
        db.Entries.AddRange(entries);
        trail.Record(db, new AuditRecord(
            ComparsaOrdersAuditActions.ComparsaOrderPrepared,
            EntityType,
            order.Id.ToString(),
            new { editionId = edition.Id, entryCount = entries.Count },
            ComparsaId: comparsaId));
        return order;
    }
}
