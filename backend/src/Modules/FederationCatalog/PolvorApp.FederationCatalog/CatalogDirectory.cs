using Microsoft.EntityFrameworkCore;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.FederationCatalog.Logos;
using PolvorApp.FederationCatalog.Persistence;

namespace PolvorApp.FederationCatalog;

/// <summary>Read-only catalogue lookups for other modules (change add-arquebusier-registry, design D2).</summary>
internal sealed class CatalogDirectory(FederationCatalogDbContext db, LogoReader reader) : ICatalogDirectory
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

    public async Task<IReadOnlyList<ComparsaSummary>> ListActiveComparsasAsync(CancellationToken cancellationToken) =>
        await db.Comparsas.AsNoTracking()
            .Where(c => c.Active)
            .Select(c => new ComparsaSummary(c.Id, c.Name, c.Side, c.Active))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<FiringChiefAssignmentSummary>> ListFiringChiefAssignmentsAsync(CancellationToken cancellationToken) =>
        await db.Assignments.AsNoTracking()
            .Where(a => db.Comparsas.Any(c => c.Id == a.ComparsaId && c.Active))
            .Select(a => new FiringChiefAssignmentSummary(a.UserId, a.ComparsaId))
            .ToListAsync(cancellationToken);

    public Task<WeaponModelSummary?> FindWeaponModelAsync(Guid weaponModelId, CancellationToken cancellationToken) =>
        db.WeaponModels.AsNoTracking()
            .Where(m => m.Id == weaponModelId)
            .Select(m => new WeaponModelSummary(m.Id, m.Kind, m.Side, m.Handedness, m.Size, m.Label, m.Active, m.Rentable))
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
            .Select(m => new WeaponModelSummary(m.Id, m.Kind, m.Side, m.Handedness, m.Size, m.Label, m.Active, m.Rentable))
            .ToListAsync(cancellationToken);
    }

    /// <summary>Applies no scope, like the rest of the directory: callers acting for a user apply BR-12 themselves.</summary>
    public async Task<LogoImage?> ReadComparsaLogoAsync(Guid comparsaId, CancellationToken cancellationToken)
    {
        // A reference without an image is logged by the reader: worth an alert, not a failed document.
        var (_, logo, image) = await reader.OpenAsync(db.Comparsas.Where(c => c.Id == comparsaId), cancellationToken);
        if (image is null || logo is null)
        {
            return null;
        }

        await using (image)
        {
            return new LogoImage(await LogoReader.ReadAllAsync(image, logo.SizeBytes, cancellationToken), logo.Width, logo.Height);
        }
    }

    public async Task<LogoImage?> ReadFederationLogoAsync(CancellationToken cancellationToken)
    {
        var (_, logo, image) = await reader.OpenAsync(db.FederationSettings, cancellationToken);
        if (image is null || logo is null)
        {
            return null;
        }

        await using (image)
        {
            return new LogoImage(await LogoReader.ReadAllAsync(image, logo.SizeBytes, cancellationToken), logo.Width, logo.Height);
        }
    }
}
