using Microsoft.EntityFrameworkCore;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.Distribution.Contracts;
using PolvorApp.Distribution.Days;
using PolvorApp.Distribution.Handovers;
using PolvorApp.Distribution.Persistence;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.FestivalEditions.Contracts;

namespace PolvorApp.Distribution.Documents;

/// <summary>
/// Reads what a day's list is built from (design D6), once per request: the printed list, the capture
/// package and the handover checks all start here, so they agree on the holders and their numbers
/// (add-offline-distribution-capture D2, D3).
/// </summary>
internal sealed class DistributionListReader(
    DistributionDbContext db,
    IOrderExports orders,
    IArquebusierRoster roster,
    ICatalogDirectory catalog)
{
    public async Task<DistributionListData> ReadAsync(DistributionDay day, EditionSnapshot edition, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(day);
        ArgumentNullException.ThrowIfNull(edition);
        var validated = await orders.ListValidatedAsync(edition.Id, cancellationToken);
        var all = validated.SelectMany(o => o.Entries).ToList();
        var proxies = await db.Proxies.AsNoTracking().Where(p => p.EditionId == edition.Id && p.Type == day.Type)
            .Select(p => new ListProxy(p.HolderEntryId, p.ProxyEntryId)).ToListAsync(cancellationToken);
        return new DistributionListData(
            edition.Year,
            day.Type,
            day.Date,
            day.Location,
            day.Slots.ToDictionary(s => s.ComparsaId, s => s.StartsAt),
            validated,
            await ComparsaNamesAsync(validated.Select(o => o.ComparsaId), cancellationToken),
            await ModelLabelsAsync(all.Select(e => e.RentalWeaponModelId).OfType<Guid>(), cancellationToken),
            await LiveAsync(all.Select(e => e.ArquebusierId), cancellationToken),
            proxies,
            day.Type == DistributionType.Powder ? await HandoversAsync(day.Id, cancellationToken) : null);
    }

    /// <summary>The powder day's recorded handovers by holder entry, for the list to fill in (D6).</summary>
    private async Task<IReadOnlyDictionary<Guid, ListHandover>> HandoversAsync(Guid dayId, CancellationToken cancellationToken)
    {
        var handovers = await db.Handovers.AsNoTracking().Where(h => h.DistributionId == dayId).ToListAsync(cancellationToken);
        return handovers.ToDictionary(
            h => h.HolderEntryId,
            h => new ListHandover(h.RentalFlaskNumber, h.Traceability1, h.Traceability2, h.CollectedBy == HandoverCollector.Proxy));
    }

    private async Task<IReadOnlyDictionary<Guid, RosterArquebusier>> LiveAsync(IEnumerable<Guid?> ids, CancellationToken cancellationToken)
    {
        Guid[] distinct = [.. ids.OfType<Guid>().Distinct()];
        return distinct.Length == 0 ? new Dictionary<Guid, RosterArquebusier>() : (await roster.FindManyAsync(distinct, cancellationToken)).ToDictionary(a => a.Id);
    }

    private async Task<IReadOnlyDictionary<Guid, string>> ComparsaNamesAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        Guid[] distinct = [.. ids.Distinct()];
        return distinct.Length == 0 ? new Dictionary<Guid, string>() : (await catalog.FindComparsasAsync(distinct, cancellationToken)).ToDictionary(c => c.Id, c => c.Name);
    }

    private async Task<IReadOnlyDictionary<Guid, string>> ModelLabelsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        Guid[] distinct = [.. ids.Distinct()];
        return distinct.Length == 0 ? new Dictionary<Guid, string>() : (await catalog.FindWeaponModelsAsync(distinct, cancellationToken)).ToDictionary(m => m.Id, m => m.Label);
    }
}
