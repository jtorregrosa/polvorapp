using Microsoft.EntityFrameworkCore;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.FederationCatalog.Persistence;

namespace PolvorApp.FederationCatalog;

/// <summary>Read-only catalogue lookups for other modules (change add-arquebusier-registry, design D2).</summary>
internal sealed class CatalogDirectory(FederationCatalogDbContext db) : ICatalogDirectory
{
    public Task<ComparsaSummary?> FindComparsaAsync(Guid comparsaId, CancellationToken cancellationToken) =>
        db.Comparsas.AsNoTracking()
            .Where(c => c.Id == comparsaId)
            .Select(c => new ComparsaSummary(c.Id, c.Name, c.Side, c.Active))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<ComparsaSummary>> FindComparsasAsync(IReadOnlyCollection<Guid> comparsaIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(comparsaIds);
        if (comparsaIds.Count == 0)
        {
            return [];
        }

        return await db.Comparsas.AsNoTracking()
            .Where(c => comparsaIds.Contains(c.Id))
            .Select(c => new ComparsaSummary(c.Id, c.Name, c.Side, c.Active))
            .ToListAsync(cancellationToken);
    }

    public Task<WeaponModelSummary?> FindWeaponModelAsync(Guid weaponModelId, CancellationToken cancellationToken) =>
        db.WeaponModels.AsNoTracking()
            .Where(m => m.Id == weaponModelId)
            .Select(m => new WeaponModelSummary(m.Id, m.Kind, m.Side, m.Handedness, m.Size, m.Label, m.Active))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<WeaponModelSummary>> FindWeaponModelsAsync(IReadOnlyCollection<Guid> weaponModelIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(weaponModelIds);
        if (weaponModelIds.Count == 0)
        {
            return [];
        }

        return await db.WeaponModels.AsNoTracking()
            .Where(m => weaponModelIds.Contains(m.Id))
            .Select(m => new WeaponModelSummary(m.Id, m.Kind, m.Side, m.Handedness, m.Size, m.Label, m.Active))
            .ToListAsync(cancellationToken);
    }
}
