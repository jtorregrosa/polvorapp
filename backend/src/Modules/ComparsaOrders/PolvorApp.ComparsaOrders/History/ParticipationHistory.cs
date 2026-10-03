using System.Collections.Frozen;
using Microsoft.EntityFrameworkCore;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.ComparsaOrders.Persistence;
using PolvorApp.FestivalEditions.Contracts;

namespace PolvorApp.ComparsaOrders.History;

/// <summary>
/// The participation history from the orders (design D5): the "first year" flag (spec: First year
/// (UC-07)) and the delete confirmation's impact (spec: Deleting an arquebusier). Read-only; entries
/// no longer linked to the registry are ignored.
/// </summary>
internal sealed class ParticipationHistory(ComparsaOrdersDbContext db, IEditionDirectory editions) : IParticipationHistory
{
    public async Task<FirstYearResult> FirstYearAsync(int editionYear, IReadOnlyCollection<Guid> arquebusierIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arquebusierIds);
        if (!await db.Orders.AsNoTracking().AnyAsync(o => o.EditionYear < editionYear, cancellationToken))
        {
            return FirstYearResult.Unknown;
        }

        Guid[] ids = [.. arquebusierIds.Distinct()];
        var veterans = ids.Length == 0
            ? []
            : await db.Entries.AsNoTracking()
                .Where(e => e.Status == ArquebusierStatus.Active && e.ArquebusierId != null && ids.Contains(e.ArquebusierId.Value))
                .Join(db.Orders.Where(o => o.EditionYear < editionYear), e => e.OrderId, o => o.Id, (e, _) => e.ArquebusierId!.Value)
                .Distinct()
                .ToListAsync(cancellationToken);
        return new FirstYearResult(true, arquebusierIds.Except(veterans).ToFrozenSet());
    }

    public async Task<DeletionImpact> GetDeletionImpactAsync(Guid arquebusierId, IReadOnlyCollection<Guid> ownedWeaponIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ownedWeaponIds);
        var current = await editions.GetCurrentAsync(cancellationToken);
        var currentId = current?.Id;
        var hasPastEntries = await db.Entries.AsNoTracking().AnyAsync(e => e.ArquebusierId == arquebusierId && e.EditionId != currentId, cancellationToken);
        if (current is null)
        {
            return new DeletionImpact(null, 0, hasPastEntries);
        }

        var entry = await db.Entries.AsNoTracking()
            .Where(e => e.ArquebusierId == arquebusierId && e.EditionId == current.Id)
            .Join(db.Orders, e => e.OrderId, o => o.Id, (_, o) => new { o.ComparsaId, o.Status })
            .SingleOrDefaultAsync(cancellationToken);
        Guid[] weaponIds = [.. ownedWeaponIds.Distinct()];
        var lentWeapons = weaponIds.Length == 0
            ? 0
            : await db.Loans.AsNoTracking()
                .Where(l => l.LenderOwnedWeaponId != null && weaponIds.Contains(l.LenderOwnedWeaponId.Value))
                .Join(db.Entries.Where(e => e.EditionId == current.Id), l => l.EntryId, e => e.Id, (l, _) => l.LenderOwnedWeaponId)
                .Distinct()
                .CountAsync(cancellationToken);
        return new DeletionImpact(
            entry is null ? null : new CurrentEntryImpact(current.Year, entry.ComparsaId, entry.Status, current.OrdersOpen),
            lentWeapons,
            hasPastEntries);
    }
}
