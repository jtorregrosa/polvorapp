using Microsoft.EntityFrameworkCore;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.ComparsaOrders.Endpoints;
using PolvorApp.ComparsaOrders.Entries;
using PolvorApp.ComparsaOrders.Orders;
using PolvorApp.ComparsaOrders.Persistence;
using PolvorApp.ComplianceInsights.Contracts;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.FestivalEditions.Contracts;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.SharedKernel.Text;

namespace PolvorApp.ComparsaOrders.Totals;

/// <summary>
/// The orders overview of an edition (spec: Order totals and dashboard (UC-16); design D10): for an
/// Admin, every active comparsa and every comparsa with an order, the status counts and the edition
/// totals; for a FiringChief, the comparsas of their scope. The edition's entries are read once and
/// totalled in memory: under a thousand rows.
/// </summary>
internal sealed class OrderOverview(
    ComparsaOrdersDbContext db,
    IComparsaScope scope,
    ICurrentUser currentUser,
    IEditionDirectory editions,
    ICatalogDirectory catalog,
    IArquebusierRoster roster,
    IComplianceRules rules)
{
    /// <summary>The overview of the edition, or of the current one when none is given; not found for a FiringChief's draft.</summary>
    public async Task<OverviewResponse?> FindAsync(Guid? editionId, CancellationToken cancellationToken)
    {
        var edition = editionId is { } id
            ? await editions.FindAsync(id, cancellationToken)
            : await editions.GetCurrentAsync(cancellationToken);
        if (edition is null || (!currentUser.IsAdmin && edition.Status == EditionStatus.Draft))
        {
            return editionId is null ? new OverviewResponse(null, [], null, null) : null;
        }

        var access = await scope.GetAccessAsync(cancellationToken);
        var orders = await access.Filter(db.Orders.AsNoTracking().Where(o => o.EditionId == edition.Id), o => o.ComparsaId).ToListAsync(cancellationToken);
        var comparsas = await ComparsasAsync(access, orders, cancellationToken);
        var totals = await TotalsByOrderAsync(orders, edition, cancellationToken);
        var labels = await LabelsAsync(totals.Values, cancellationToken);
        var byComparsa = orders.ToDictionary(o => o.ComparsaId);
        foreach (var missing in byComparsa.Keys.Where(id => comparsas.All(c => c.Id != id)))
        {
            // The rows must add up to the edition totals; the catalogue never deletes a comparsa with orders.
            throw new InvalidOperationException($"Comparsa {missing} of an order is missing from the catalogue.");
        }

        var rows = comparsas
            .OrderBy(c => c.Name, SpanishOrder.Names)
            .ThenBy(c => c.Id)
            .Select(c => byComparsa.GetValueOrDefault(c.Id) is { } order
                ? new OverviewRowResponse(Comparsa(c), order.Id, order.Status, OrderTotalsResponse.From(totals[order.Id], labels), CanPrepare: false)
                : new OverviewRowResponse(Comparsa(c), null, null, null, CanPrepare(c, edition)))
            .ToList();
        var editionResponse = new OrderEditionResponse(edition.Id, edition.Year, edition.Status, edition.OrdersOpen);
        return currentUser.IsAdmin
            ? new OverviewResponse(editionResponse, rows, StatusCounts(rows), OrderTotalsResponse.From(OrderTotals.Sum(totals.Values), labels))
            : new OverviewResponse(editionResponse, rows, null, null);
    }

    /// <summary>Admins: the active comparsas plus those with an order. FiringChiefs: their scope.</summary>
    private async Task<List<ComparsaSummary>> ComparsasAsync(ComparsaAccess access, List<ComparsaOrder> orders, CancellationToken cancellationToken)
    {
        if (!access.IsAll)
        {
            return [.. await catalog.FindComparsasAsync([.. access.ComparsaIds], cancellationToken)];
        }

        var active = await catalog.ListActiveComparsasAsync(cancellationToken);
        Guid[] others = [.. orders.Select(o => o.ComparsaId).Where(id => active.All(c => c.Id != id)).Distinct()];
        return [.. active, .. others.Length == 0 ? [] : await catalog.FindComparsasAsync(others, cancellationToken)];
    }

    private async Task<Dictionary<Guid, OrderTotals>> TotalsByOrderAsync(List<ComparsaOrder> orders, EditionSnapshot edition, CancellationToken cancellationToken)
    {
        Guid[] orderIds = [.. orders.Select(o => o.Id)];
        var entries = orderIds.Length == 0
            ? []
            : await db.Entries.AsNoTracking().Where(e => orderIds.Contains(e.OrderId)).ToListAsync(cancellationToken);
        var withWarnings = await EntriesWithWarningsAsync(entries, edition, cancellationToken);
        var byOrder = entries.ToLookup(e => e.OrderId);
        return orders.ToDictionary(o => o.Id, o => OrderTotals.Of([.. byOrder[o.Id]], withWarnings));
    }

    /// <summary>
    /// The <c>ACTIVE</c> entries of the edition in progress whose arquebusier has a compliance warning on
    /// the festival dates. The overview takes no lock: an arquebusier deleted after the entries were read
    /// counts as no longer in the registry, without warnings, as their entry will once its link is nulled.
    /// </summary>
    private async Task<HashSet<Guid>> EntriesWithWarningsAsync(List<EditionEntry> entries, EditionSnapshot edition, CancellationToken cancellationToken)
    {
        if (edition.Status != EditionStatus.InProgress)
        {
            return [];
        }

        var active = entries.Where(e => e.Status == ArquebusierStatus.Active && e.ArquebusierId is not null).ToList();
        Guid[] ids = [.. active.Select(e => e.ArquebusierId!.Value).Distinct()];
        var live = ids.Length == 0 ? [] : (await roster.FindManyAsync(ids, cancellationToken)).ToDictionary(a => a.Id);
        return [.. active
            .Where(e => live.TryGetValue(e.ArquebusierId!.Value, out var a)
                && rules.EvaluateForFestival(EntryCopies.FactsOf(a), edition.FestivalStartsOn, edition.FestivalEndsOn).Count > 0)
            .Select(e => e.Id)];
    }

    private async Task<Dictionary<Guid, string>> LabelsAsync(IEnumerable<OrderTotals> totals, CancellationToken cancellationToken)
    {
        Guid[] ids = [.. totals.SelectMany(t => t.WeaponRentals.Keys).Distinct()];
        return ids.Length == 0 ? [] : (await catalog.FindWeaponModelsAsync(ids, cancellationToken)).ToDictionary(m => m.Id, m => m.Label);
    }

    private bool CanPrepare(ComparsaSummary comparsa, EditionSnapshot edition) =>
        comparsa.Active && (currentUser.IsAdmin ? edition.Status != EditionStatus.Draft : edition.OrdersOpen);

    private static OrderComparsaResponse Comparsa(ComparsaSummary comparsa) => new(comparsa.Id, comparsa.Name, comparsa.Side);

    private static OverviewStatusCountsResponse StatusCounts(List<OverviewRowResponse> rows) => new(
        rows.Count(r => r.Status is null),
        rows.Count(r => r.Status == OrderStatus.Draft),
        rows.Count(r => r.Status == OrderStatus.Submitted),
        rows.Count(r => r.Status == OrderStatus.Returned),
        rows.Count(r => r.Status == OrderStatus.Validated));
}
