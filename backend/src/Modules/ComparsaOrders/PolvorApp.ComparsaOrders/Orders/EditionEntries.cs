using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.ComparsaOrders.Entries;
using PolvorApp.ComparsaOrders.Persistence;

namespace PolvorApp.ComparsaOrders.Orders;

/// <summary>
/// The entries as other modules read them (add-distribution-planning, design D3); unscoped, read-only.
/// Each entry is read with its order in one query.
/// </summary>
internal sealed class EditionEntries(ComparsaOrdersDbContext db) : IEditionEntries
{
    public async Task<IReadOnlyList<EditionEntryFacts>> ListAsync(Guid editionId, Guid comparsaId, CancellationToken cancellationToken) =>
        await Facts(db.Orders.Where(o => o.EditionId == editionId && o.ComparsaId == comparsaId)).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<EditionEntryFacts>> FindManyAsync(IReadOnlyCollection<Guid> entryIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entryIds);
        if (entryIds.Count == 0)
        {
            return [];
        }

        Guid[] ids = [.. entryIds.Distinct()];
        if (ids.Length > IEditionEntries.MaxIds)
        {
            throw new ArgumentOutOfRangeException(nameof(entryIds), ids.Length, $"At most {IEditionEntries.MaxIds} entries are read at once.");
        }

        return await Facts(db.Orders, entry => ids.Contains(entry.Id)).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, OrderStatus>> ListOrderStatusesAsync(Guid editionId, CancellationToken cancellationToken) =>
        await db.Orders.AsNoTracking().Where(o => o.EditionId == editionId)
            .Select(o => new { o.ComparsaId, o.Status })
            .ToDictionaryAsync(o => o.ComparsaId, o => o.Status, cancellationToken);

    private IQueryable<EditionEntryFacts> Facts(IQueryable<ComparsaOrder> orders, Expression<Func<EditionEntry, bool>>? filter = null)
    {
        var entries = db.Entries.AsNoTracking();
        if (filter is not null)
        {
            entries = entries.Where(filter);
        }

        return entries.Join(
            orders.AsNoTracking(),
            entry => entry.OrderId,
            order => order.Id,
            (entry, order) => new EditionEntryFacts(
                entry.Id,
                order.Id,
                order.EditionId,
                order.ComparsaId,
                order.Status,
                entry.ArquebusierId,
                entry.Status == ArquebusierStatus.Active,
                entry.PowderKg,
                entry.WeaponSource,
                entry.RentalWeaponModelId,
                entry.Flask,
                new ExportedPerson(entry.FirstName, entry.LastName, entry.NationalId, entry.FederationId)));
    }
}
