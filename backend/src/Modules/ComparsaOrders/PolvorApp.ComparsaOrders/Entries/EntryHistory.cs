using Microsoft.EntityFrameworkCore;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ComparsaOrders.Orders;
using PolvorApp.ComparsaOrders.Persistence;

namespace PolvorApp.ComparsaOrders.Entries;

/// <summary>
/// What a new entry needs from the orders already stored (specs: Preparing an order, Adding arquebusiers
/// to an order, Pre-fill from the previous edition (BR-11); design D8): who already has an entry in the
/// edition, and each arquebusier's previous entry.
/// </summary>
internal sealed class EntryHistory(ComparsaOrdersDbContext db)
{
    /// <summary>A new entry for <paramref name="arquebusier"/>, pre-filled and with its history copy.</summary>
    public static EditionEntry NewEntry(
        ComparsaOrder order, RosterArquebusier arquebusier, EntryValues? previous, IReadOnlySet<Guid> offeredModelIds, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(arquebusier);
        var entry = new EditionEntry
        {
            Id = Guid.CreateVersion7(),
            OrderId = order.Id,
            EditionId = order.EditionId,
            ArquebusierId = arquebusier.Id,
            CreatedAt = now,
            CopiedAt = now,
        };
        Prefill.For(arquebusier.Status, [.. arquebusier.Weapons.Select(w => w.Id)], previous, offeredModelIds).ApplyTo(entry);
        EntryCopies.Refresh(entry, arquebusier, now);
        return entry;
    }

    /// <summary>The arquebusiers who have no entry in the edition yet, in the given order.</summary>
    public async Task<List<RosterArquebusier>> WithoutEntryAsync(
        Guid editionId, IReadOnlyList<RosterArquebusier> arquebusiers, CancellationToken cancellationToken)
    {
        Guid[] ids = [.. arquebusiers.Select(a => a.Id)];
        var taken = (await db.Entries.AsNoTracking()
            .Where(e => e.EditionId == editionId && e.ArquebusierId != null && ids.Contains(e.ArquebusierId.Value))
            .Select(e => e.ArquebusierId!.Value)
            .ToListAsync(cancellationToken))
            .ToHashSet();
        return [.. arquebusiers.Where(a => !taken.Contains(a.Id))];
    }

    /// <summary>Each arquebusier's entry in the latest edition with an earlier year in which they have one (BR-11: choices only).</summary>
    public async Task<Dictionary<Guid, EntryValues>> PreviousEntriesAsync(Guid[] arquebusierIds, int year, CancellationToken cancellationToken)
    {
        if (arquebusierIds.Length == 0)
        {
            return [];
        }

        // Only the value columns: the history copy of past entries is never read here.
        var rows = await db.Entries.AsNoTracking()
            .Where(e => e.ArquebusierId != null && arquebusierIds.Contains(e.ArquebusierId.Value))
            .Join(db.Orders.AsNoTracking().Where(o => o.EditionYear < year), e => e.OrderId, o => o.Id, (e, o) => new PreviousRow(
                e.ArquebusierId!.Value,
                o.EditionYear,
                new EntryValues(e.Status, e.PowderKg, e.CapsBoxes, e.CapsType, e.WeaponSource, e.OwnedWeaponId, e.RentalWeaponModelId, e.Flask)))
            .ToListAsync(cancellationToken);
        return rows
            .GroupBy(r => r.ArquebusierId)
            .ToDictionary(g => g.Key, g => g.MaxBy(r => r.EditionYear)!.Values);
    }

    private sealed record PreviousRow(Guid ArquebusierId, int EditionYear, EntryValues Values);
}
