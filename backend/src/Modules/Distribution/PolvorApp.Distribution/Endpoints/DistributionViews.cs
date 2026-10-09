using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.Distribution.Contracts;
using PolvorApp.Distribution.Days;
using PolvorApp.Distribution.Documents;
using PolvorApp.Distribution.Persistence;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.FestivalEditions.Contracts;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.SharedKernel.Text;

namespace PolvorApp.Distribution.Endpoints;

/// <summary>
/// Reads the distribution plan for the caller (spec: Distribution visibility (BR-12); design D7): Admins
/// see every slot, the comparsas without one and the orders not validated; FiringChiefs see the days and
/// their own comparsas' slots, and never a draft edition.
/// </summary>
internal sealed class DistributionViews(
    DistributionDbContext db,
    IEditionDirectory editions,
    ICatalogDirectory catalog,
    IEditionEntries entries,
    IComparsaScope scope,
    ICurrentUser currentUser,
    DistributionListReader lists)
{
    public async Task<DistributionPlanResponse?> PlanAsync(Guid editionId, CancellationToken cancellationToken)
    {
        var edition = await editions.FindAsync(editionId, cancellationToken);
        if (edition is null || (!currentUser.IsAdmin && edition.Status == EditionStatus.Draft))
        {
            return null;
        }

        var access = await scope.GetAccessAsync(cancellationToken);
        var days = await db.Days.AsNoTracking().Include(d => d.Slots).Where(d => d.EditionId == editionId).OrderBy(d => d.Type).ToListAsync(cancellationToken);
        var active = await catalog.ListActiveComparsasAsync(cancellationToken);
        var names = await NamesAsync(days.SelectMany(d => d.Slots).Select(s => s.ComparsaId), active, cancellationToken);
        var views = new List<DistributionDayResponse>(days.Count);
        foreach (var day in days)
        {
            var counts = currentUser.IsAdmin && day.Type == DistributionType.Powder && edition.Status == EditionStatus.InProgress
                ? await CountsAsync(day, edition, cancellationToken)
                : null;
            views.Add(Day(day, access, names, active) with { Handovers = counts });
        }

        IReadOnlyList<NotValidatedResponse>? notValidated = null;
        if (currentUser.IsAdmin)
        {
            var statuses = await entries.ListOrderStatusesAsync(editionId, cancellationToken);
            notValidated = [.. active
                .Where(c => !statuses.TryGetValue(c.Id, out var status) || status != OrderStatus.Validated)
                .OrderBy(c => c.Name, SpanishOrder.Names)
                .Select(c => new NotValidatedResponse(c.Id, c.Name, statuses.TryGetValue(c.Id, out var status) ? status : null))];
        }

        var inProgress = edition.Status == EditionStatus.InProgress;
        var canManageProxies = currentUser.IsAdmin ? edition.Status != EditionStatus.Draft : inProgress;
        return new DistributionPlanResponse(edition.Id, edition.Year, edition.Status, views, currentUser.IsAdmin && inProgress, canManageProxies, notValidated);
    }

    /// <summary>
    /// A day as the Admin-only writes return it: every slot and the comparsas without one. Never call it
    /// for a FiringChief (BR-12): use <see cref="PlanAsync"/>, which applies the scope.
    /// </summary>
    public async Task<DistributionDayResponse> AdminDayAsync(DistributionDay day, CancellationToken cancellationToken)
    {
        var active = await catalog.ListActiveComparsasAsync(cancellationToken);
        var names = await NamesAsync(day.Slots.Select(s => s.ComparsaId), active, cancellationToken);
        return Day(day, ComparsaAccess.All, names, active);
    }

    private static DistributionDayResponse Day(DistributionDay day, ComparsaAccess access, IReadOnlyDictionary<Guid, string> names, IReadOnlyList<ComparsaSummary> active)
    {
        var slots = day.Slots
            .Where(s => access.CanAccess(s.ComparsaId))
            .Select(s => new SlotResponse(s.ComparsaId, names[s.ComparsaId], s.StartsAt.ToString("HH:mm", CultureInfo.InvariantCulture)))
            .OrderBy(s => s.StartsAt, StringComparer.Ordinal)
            .ThenBy(s => s.ComparsaName, SpanishOrder.Names)
            .ToList();
        var withSlot = day.Slots.Select(s => s.ComparsaId).ToHashSet();
        IReadOnlyList<ComparsaRef>? withoutSlot = access.IsAll
            ? [.. active.Where(c => !withSlot.Contains(c.Id)).OrderBy(c => c.Name, SpanishOrder.Names).Select(c => new ComparsaRef(c.Id, c.Name))]
            : null;
        return new DistributionDayResponse(day.Id, day.EditionId, day.Type, day.Date, day.Location, day.Version, slots, withoutSlot);
    }

    /// <summary>The powder day's handovers out of its list's holders (spec: Handover screens).</summary>
    private async Task<HandoverCountResponse> CountsAsync(DistributionDay day, EditionSnapshot edition, CancellationToken cancellationToken)
    {
        var holders = DistributionLists.Rows(await lists.ReadAsync(day, edition, cancellationToken), DistributionTexts.Spanish).Rows.Count;
        var recorded = await db.Handovers.CountAsync(h => h.DistributionId == day.Id, cancellationToken);
        return new HandoverCountResponse(recorded, holders);
    }

    /// <summary>Names of the comparsas with a slot, inactive ones included.</summary>
    private async Task<IReadOnlyDictionary<Guid, string>> NamesAsync(IEnumerable<Guid> comparsaIds, IReadOnlyList<ComparsaSummary> active, CancellationToken cancellationToken)
    {
        var names = active.ToDictionary(c => c.Id, c => c.Name);
        Guid[] missing = [.. comparsaIds.Distinct().Where(id => !names.ContainsKey(id))];
        foreach (var comparsa in missing.Length == 0 ? [] : await catalog.FindComparsasAsync(missing, cancellationToken))
        {
            names[comparsa.Id] = comparsa.Name;
        }

        return names;
    }
}
