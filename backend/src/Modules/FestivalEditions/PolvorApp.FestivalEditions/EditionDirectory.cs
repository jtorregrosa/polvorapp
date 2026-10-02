using Microsoft.EntityFrameworkCore;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.FestivalEditions.Contracts;
using PolvorApp.FestivalEditions.Editions;
using PolvorApp.FestivalEditions.Persistence;

namespace PolvorApp.FestivalEditions;

/// <summary>
/// Read-only edition lookups for other modules, e.g. comparsa orders (design D7). Drafts are
/// returned too: a caller acting for a FiringChief must treat a <see cref="EditionStatus.Draft"/> as
/// not found (BR-12). The reads are not one snapshot; #10 rereads inside its own write transaction.
/// </summary>
internal sealed class EditionDirectory(FestivalEditionsDbContext db, ICatalogDirectory catalog) : IEditionDirectory
{
    public async Task<EditionSnapshot?> GetCurrentAsync(CancellationToken cancellationToken) =>
        await db.Editions.AsNoTracking().SingleOrDefaultAsync(e => e.Status == EditionStatus.InProgress, cancellationToken) is { } edition
            ? await SnapshotAsync(edition, cancellationToken)
            : null;

    public async Task<EditionSnapshot?> FindAsync(Guid editionId, CancellationToken cancellationToken) =>
        await db.Editions.AsNoTracking().SingleOrDefaultAsync(e => e.Id == editionId, cancellationToken) is { } edition
            ? await SnapshotAsync(edition, cancellationToken)
            : null;

    private async Task<EditionSnapshot> SnapshotAsync(FestivalEdition edition, CancellationToken cancellationToken)
    {
        var ids = await db.EditionWeaponModels.AsNoTracking()
            .Where(m => m.EditionId == edition.Id)
            .Select(m => m.WeaponModelId)
            .ToListAsync(cancellationToken);
        var models = await catalog.FindWeaponModelsAsync(ids, cancellationToken);
        if (models.Count != ids.Count)
        {
            // The foreign key prevents this; orders must never run on a silently truncated offer.
            throw new InvalidOperationException($"Edition {edition.Id} references weapon models the catalogue does not have.");
        }

        return new EditionSnapshot(
            edition.Id,
            edition.Year,
            edition.Status,
            edition.Status == EditionStatus.InProgress && edition.OrdersOpen,
            edition.FestivalStartsOn,
            edition.FestivalEndsOn,
            models.Where(m => m.Active && m.Rentable).Select(m => m.Id).Order().ToList());
    }
}
