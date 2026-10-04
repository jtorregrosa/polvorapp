using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.ComparsaOrders.Loans;
using PolvorApp.ComparsaOrders.Orders;
using PolvorApp.ComparsaOrders.Persistence;
using PolvorApp.FestivalEditions.Contracts;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.SharedKernel.Auditing;

namespace PolvorApp.ComparsaOrders.Entries;

/// <summary>
/// Writes to the entries of an order (specs: Adding arquebusiers to an order (UC-12), Edition entries
/// (BR-05, BR-07), Weapon loans (UC-13, BR-09), Who may edit orders (BR-10), Entry history (BR-14);
/// design D8). Each write reads the edition <c>FOR SHARE</c>, then locks the order <c>FOR UPDATE</c>
/// within the caller's scope, refreshes the history copy, touches the order and is audited in the same
/// transaction. No entry is ever removed.
/// </summary>
internal sealed class EntryAdministration(
    ComparsaOrdersDbContext db,
    OrderGate gate,
    OrderWriteGuard guard,
    IComparsaScope scope,
    IArquebusierRoster roster,
    EntryHistory history,
    LoanWriter loans,
    IAuditTrail trail,
    TimeProvider time)
{
    public const string EntityType = "EditionEntry";

    /// <summary>Adds a pre-filled entry for an arquebusier of the order's comparsa who has none in the edition.</summary>
    public Task<OrderResult<ComparsaOrder>> AddAsync(Guid orderId, Guid arquebusierId, CancellationToken cancellationToken) =>
        WriteAsync(nameof(AddAsync), orderId, async (order, edition) =>
        {
            // Scoped to the order's comparsa: an id from the request never reaches another comparsa's data.
            var arquebusier = (await roster.ListByComparsaAsync(order.ComparsaId, cancellationToken)).SingleOrDefault(a => a.Id == arquebusierId);
            if (arquebusier is null)
            {
                return Change.Failed(OrderOutcome.NotFound);
            }

            if ((await history.WithoutEntryAsync(order.EditionId, [arquebusier], cancellationToken)).Count == 0)
            {
                return Change.Failed(OrderOutcome.AlreadyInEdition);
            }

            var now = time.GetUtcNow();
            var previous = await history.PreviousEntriesAsync([arquebusier.Id], order.EditionYear, cancellationToken);
            var entry = EntryHistory.NewEntry(order, arquebusier, previous.GetValueOrDefault(arquebusier.Id), edition.OfferedWeaponModelIds.ToHashSet(), now);
            db.Entries.Add(entry);
            var statusChange = Touch(order, now);
            Record(ComparsaOrdersAuditActions.EditionEntryAdded, entry, order, new { orderId = order.Id, arquebusierId, orderStatus = statusChange });
            return Change.Saved;
        }, cancellationToken);

    /// <summary>Replaces the values and the loan of an entry, if its version is still current.</summary>
    public Task<OrderResult<ComparsaOrder>> UpdateAsync(Guid orderId, Guid entryId, uint version, EntryFields fields, CancellationToken cancellationToken) =>
        WriteAsync(nameof(UpdateAsync), orderId, async (order, edition) =>
        {
            var entry = await db.Entries.SingleOrDefaultAsync(e => e.Id == entryId && e.OrderId == order.Id, cancellationToken);
            if (entry is null)
            {
                return Change.Failed(OrderOutcome.EntryNotFound);
            }

            // The order lock serialises entry writes, so this compares against the committed version; the
            // concurrency token still catches a registry SET NULL between this load and the save.
            if (version != entry.Version)
            {
                return Change.Failed(OrderOutcome.EntryModified);
            }

            // An erased person's entry is history only (spec: Erased entries).
            if (entry.ErasedAt is not null)
            {
                return Change.Failed(OrderOutcome.EntryErased);
            }

            var arquebusier = await LiveArquebusierAsync(entry, cancellationToken);
            var (input, errors) = EntryInput.Read(fields, await ContextAsync(entry, arquebusier, edition, cancellationToken));
            if (input is null)
            {
                return new Change(OrderOutcome.Invalid, errors);
            }

            // A loan whose lender was erased cannot be edited; changing the weapon source drops it.
            if (input.Values.WeaponSource == WeaponSource.Loan
                && await db.Loans.AnyAsync(l => l.EntryId == entry.Id && l.ErasedAt != null, cancellationToken))
            {
                return Change.Failed(OrderOutcome.EntryErased);
            }

            var now = time.GetUtcNow();
            var changed = ChangedFields(EntryValues.Of(entry), input.Values);
            input.Values.ApplyTo(entry);
            var (loanErrors, loanChanged) = await loans.ApplyAsync(entry, input.Loan, now, cancellationToken);
            if (loanErrors is not null)
            {
                return new Change(OrderOutcome.Invalid, loanErrors);
            }

            if (loanChanged)
            {
                changed.Add("loan");
            }

            if (changed.Count == 0)
            {
                return Change.Unchanged;
            }

            RefreshCopy(entry, arquebusier, now);
            var statusChange = Touch(order, now);
            Record(ComparsaOrdersAuditActions.EditionEntryUpdated, entry, order, new { orderId = order.Id, fields = changed, orderStatus = statusChange });
            return Change.Saved;
        }, cancellationToken);

    /// <summary>What the field rules need to know about the entry (design D8).</summary>
    private async Task<EntryContext> ContextAsync(
        EditionEntry entry, RosterArquebusier? arquebusier, EditionSnapshot edition, CancellationToken cancellationToken) =>
        new(
            arquebusier?.Weapons.Select(w => w.Id).ToHashSet(),
            edition.OfferedWeaponModelIds.ToHashSet(),
            KeepsRemovedWeapon: entry is { WeaponSource: WeaponSource.Owned, OwnedWeaponId: null },
            HasLoan: await loans.HasLoanAsync(entry.Id, cancellationToken));

    /// <summary>The history copy (design D3): from the registry while the arquebusier is in it, else kept, minus an owned weapon no longer used.</summary>
    private static void RefreshCopy(EditionEntry entry, RosterArquebusier? arquebusier, DateTimeOffset now)
    {
        if (arquebusier is not null)
        {
            EntryCopies.Refresh(entry, arquebusier, now);
        }
        else if (entry.WeaponSource != WeaponSource.Owned)
        {
            EntryCopies.ClearOwnedWeapon(entry);
        }
    }

    /// <summary>
    /// The shared shape of an entry write (design D8): the scope, then the edition (<c>FOR SHARE</c>,
    /// BR-10), then the order (<c>FOR UPDATE</c>, scoped), then the change, then one save and commit.
    /// This is the module's lock order (design D3). Every refusal, scope included, goes through the
    /// guard, so it is logged.
    /// </summary>
    private Task<OrderResult<ComparsaOrder>> WriteAsync(
        string operation, Guid orderId, Func<ComparsaOrder, EditionSnapshot, Task<Change>> change, CancellationToken cancellationToken) =>
        guard.RunAsync(operation, orderId, async () =>
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
            var orderOutcome = gate.CheckOrder(order);
            if (orderOutcome != OrderOutcome.Done)
            {
                return OrderResult<ComparsaOrder>.Failed(orderOutcome);
            }

            var result = await change(order, edition);
            switch (result)
            {
                case { Outcome: OrderOutcome.Invalid }:
                    return OrderResult<ComparsaOrder>.Invalid(result.Errors ?? new Dictionary<string, string>());
                case { Outcome: not OrderOutcome.Done }:
                    return OrderResult<ComparsaOrder>.Failed(result.Outcome);
                case { IsUnchanged: true }:
                    // Nothing to save, audit or refresh: a save that changes nothing records nothing.
                    return OrderResult<ComparsaOrder>.Done(order);
            }

            var saved = await db.SaveAsync(guard, orderId, onEntryIndex: OrderOutcome.AlreadyInEdition, cancellationToken);
            if (saved != OrderOutcome.Done)
            {
                return OrderResult<ComparsaOrder>.Failed(saved);
            }

            await transaction.CommitAsync(cancellationToken);
            return OrderResult<ComparsaOrder>.Done(order);
        });

    /// <summary>
    /// The entry's arquebusier from the registry, by the entry's own stored link (never an id from the
    /// request); null once they left it. A link without an arquebusier breaks design D3.
    /// </summary>
    private async Task<RosterArquebusier?> LiveArquebusierAsync(EditionEntry entry, CancellationToken cancellationToken) =>
        entry.ArquebusierId is not { } id ? null
        : (await roster.FindManyAsync([id], cancellationToken)).SingleOrDefault()
            ?? throw new InvalidOperationException($"Entry {entry.Id} links to an arquebusier the registry does not have.");

    /// <summary>Touches the order (design D8) and returns its status change, if the edit sent a submitted order back to draft.</summary>
    private object? Touch(ComparsaOrder order, DateTimeOffset now)
    {
        var previous = order.Status;
        gate.Touch(order, now);
        return previous == order.Status ? null : new { previous, current = order.Status };
    }

    private void Record(string action, EditionEntry entry, ComparsaOrder order, object data) =>
        trail.Record(db, new AuditRecord(action, EntityType, entry.Id.ToString(), data, ComparsaId: order.ComparsaId));

    /// <summary>The names of the fields an edit changes, in a stable order; the values are not recorded.</summary>
    private static List<string> ChangedFields(EntryValues current, EntryValues next)
    {
        var changes = new (string Field, bool Changed)[]
        {
            ("status", current.Status != next.Status),
            ("powderKg", current.PowderKg != next.PowderKg),
            ("capsBoxes", current.CapsBoxes != next.CapsBoxes),
            ("capsType", current.CapsType != next.CapsType),
            ("weaponSource", current.WeaponSource != next.WeaponSource),
            ("ownedWeaponId", current.OwnedWeaponId != next.OwnedWeaponId),
            ("rentalWeaponModelId", current.RentalWeaponModelId != next.RentalWeaponModelId),
            ("flask", current.Flask != next.Flask),
        };
        return [.. changes.Where(c => c.Changed).Select(c => c.Field)];
    }

    /// <summary>What a change did: saved, unchanged (nothing to save or audit), or refused.</summary>
    private sealed record Change(OrderOutcome Outcome, IReadOnlyDictionary<string, string>? Errors = null, bool IsUnchanged = false)
    {
        public static readonly Change Saved = new(OrderOutcome.Done);
        public static readonly Change Unchanged = new(OrderOutcome.Done, IsUnchanged: true);

        public static Change Failed(OrderOutcome outcome) => new(outcome);
    }
}
