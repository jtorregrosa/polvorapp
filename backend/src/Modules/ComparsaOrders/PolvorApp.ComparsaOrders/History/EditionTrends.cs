using Microsoft.EntityFrameworkCore;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.ComparsaOrders.Persistence;
using PolvorApp.FestivalEditions.Contracts;

namespace PolvorApp.ComparsaOrders.History;

/// <summary>
/// <see cref="IEditionTrends"/> (add-statistics-trends, design D1): the counts of up to 10 editions in
/// one grouped query by edition and comparsa, the rentals by model in another, and the first year from
/// each arquebusier's first <c>ACTIVE</c> year. Read-only.
/// </summary>
internal sealed class EditionTrends(ComparsaOrdersDbContext db, IEditionDirectory editions) : IEditionTrends
{
    /// <summary>How many editions a trend covers (spec: Edition trends (UC-07)).</summary>
    public const int Editions = 10;

    public async Task<IReadOnlyList<EditionTrendRow>> ListAsync(IReadOnlyCollection<Guid>? comparsaIds, CancellationToken cancellationToken)
    {
        var started = await editions.ListStartedAsync(Editions, cancellationToken);
        if (started.Count == 0)
        {
            return [];
        }

        Guid[] editionIds = [.. started.Select(e => e.Id)];
        var scoped = Scoped(editionIds, comparsaIds);
        var counts = await scoped
            .GroupBy(x => new { x.EditionId, x.ComparsaId })
            .Select(g => new
            {
                g.Key.EditionId,
                g.Key.ComparsaId,
                Active = g.Count(x => x.Status == ArquebusierStatus.Active),
                Reserve = g.Count(x => x.Status == ArquebusierStatus.Reserve),
                PowderKg = g.Sum(x => x.PowderKg),
                CapsBoxes = g.Sum(x => x.CapsBoxes),
                Owned = g.Count(x => x.Status == ArquebusierStatus.Active && x.WeaponSource == WeaponSource.Owned),
                Rental = g.Count(x => x.Status == ArquebusierStatus.Active && x.WeaponSource == WeaponSource.Rental),
                Loan = g.Count(x => x.Status == ArquebusierStatus.Active && x.WeaponSource == WeaponSource.Loan),
                NoWeapon = g.Count(x => x.Status == ArquebusierStatus.Active && x.WeaponSource == WeaponSource.None),
                FlaskRentals = g.Count(x => x.Flask == FlaskOption.Rental1Kg || x.Flask == FlaskOption.Rental2Kg),
            })
            .ToListAsync(cancellationToken);
        var rentals = await scoped
            .Where(x => x.Status == ArquebusierStatus.Active && x.WeaponSource == WeaponSource.Rental && x.RentalWeaponModelId != null)
            .GroupBy(x => new { x.EditionId, ModelId = x.RentalWeaponModelId!.Value })
            .Select(g => new { g.Key.EditionId, g.Key.ModelId, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var active = await scoped
            .Where(x => x.Status == ArquebusierStatus.Active && x.ArquebusierId != null)
            .Select(x => new { x.EditionId, ArquebusierId = x.ArquebusierId!.Value })
            .ToListAsync(cancellationToken);
        var firstActiveYear = await FirstActiveYearsAsync([.. active.Select(a => a.ArquebusierId).Distinct()], cancellationToken);
        var firstOrderYear = await db.Orders.AsNoTracking().MinAsync(o => (int?)o.EditionYear, cancellationToken);

        return [.. started.Select(edition =>
        {
            var groups = counts.Where(c => c.EditionId == edition.Id).ToList();
            List<Guid> ids = [.. active.Where(a => a.EditionId == edition.Id).Select(a => a.ArquebusierId)];
            return new EditionTrendRow(
                edition.Year,
                edition.Status == EditionStatus.InProgress,
                groups.Sum(g => g.Active),
                groups.Sum(g => g.Reserve),
                groups.Sum(g => g.PowderKg),
                groups.Sum(g => g.CapsBoxes),
                groups.Sum(g => g.Owned),
                groups.Sum(g => g.Rental),
                groups.Sum(g => g.Loan),
                groups.Sum(g => g.NoWeapon),
                rentals.Where(r => r.EditionId == edition.Id).ToDictionary(r => r.ModelId, r => r.Count),
                groups.Sum(g => g.FlaskRentals),
                // Known only once an earlier edition has orders (maintainer decision, as the first-year flag).
                firstOrderYear < edition.Year ? ids.Count(id => firstActiveYear.GetValueOrDefault(id) == edition.Year) : null,
                groups.Where(g => g.Active > 0).ToDictionary(g => g.ComparsaId, g => g.Active),
                ids);
        })];
    }

    /// <summary>The entries of the orders of <paramref name="editionIds"/>, of <paramref name="comparsaIds"/> or of every comparsa.</summary>
    private IQueryable<ScopedEntry> Scoped(Guid[] editionIds, IReadOnlyCollection<Guid>? comparsaIds)
    {
        var entries = db.Entries.AsNoTracking()
            .Join(db.Orders.AsNoTracking(), e => e.OrderId, o => o.Id, (e, o) => new ScopedEntry
            {
                EditionId = o.EditionId,
                ComparsaId = o.ComparsaId,
                ArquebusierId = e.ArquebusierId,
                Status = e.Status,
                PowderKg = e.PowderKg,
                CapsBoxes = e.CapsBoxes,
                WeaponSource = e.WeaponSource,
                RentalWeaponModelId = e.RentalWeaponModelId,
                Flask = e.Flask,
            })
            .Where(x => editionIds.Contains(x.EditionId));
        if (comparsaIds is not null)
        {
            Guid[] wanted = [.. comparsaIds];
            entries = entries.Where(x => wanted.Contains(x.ComparsaId));
        }

        return entries;
    }

    /// <summary>The year of each arquebusier's first <c>ACTIVE</c> entry in any comparsa (spec: First year (UC-07)).</summary>
    private async Task<Dictionary<Guid, int>> FirstActiveYearsAsync(Guid[] arquebusierIds, CancellationToken cancellationToken) =>
        arquebusierIds.Length == 0
            ? []
            : await db.Entries.AsNoTracking()
                .Where(e => e.Status == ArquebusierStatus.Active && e.ArquebusierId != null && arquebusierIds.Contains(e.ArquebusierId.Value))
                .Join(db.Orders, e => e.OrderId, o => o.Id, (e, o) => new { ArquebusierId = e.ArquebusierId!.Value, o.EditionYear })
                .GroupBy(x => x.ArquebusierId)
                .Select(g => new { ArquebusierId = g.Key, Year = g.Min(x => x.EditionYear) })
                .ToDictionaryAsync(x => x.ArquebusierId, x => x.Year, cancellationToken);

    /// <summary>An entry with its order's edition and comparsa, for the grouped counts.</summary>
    private sealed class ScopedEntry
    {
        public Guid EditionId { get; init; }

        public Guid ComparsaId { get; init; }

        public Guid? ArquebusierId { get; init; }

        public ArquebusierStatus Status { get; init; }

        public int PowderKg { get; init; }

        public int CapsBoxes { get; init; }

        public WeaponSource WeaponSource { get; init; }

        public Guid? RentalWeaponModelId { get; init; }

        public FlaskOption Flask { get; init; }
    }
}
