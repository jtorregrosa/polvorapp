using Microsoft.EntityFrameworkCore;
using PolvorApp.FederationCatalog.Persistence;
using PolvorApp.SharedKernel.Storage;

namespace PolvorApp.FederationCatalog.Logos;

/// <summary>Tells the platform's orphan sweep which stored logos (comparsas' and the Federation's) the catalogue still references (design D5).</summary>
internal sealed class CatalogLogoOwner(FederationCatalogDbContext db) : IStoredObjectOwner
{
    public string Prefix => LogoStorage.Prefix;

    public async Task<IReadOnlySet<string>> FilterReferencedAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken)
    {
        var referenced = await db.Comparsas.AsNoTracking()
            .Where(c => c.Logo != null && keys.Contains(c.Logo.ObjectKey))
            .Select(c => c.Logo!.ObjectKey)
            .ToListAsync(cancellationToken);
        // The Federation's logo shares the prefix (add-distribution-planning, design D11).
        var federation = await db.FederationSettings.AsNoTracking()
            .Where(s => s.Logo != null && keys.Contains(s.Logo.ObjectKey))
            .Select(s => s.Logo!.ObjectKey)
            .ToListAsync(cancellationToken);
        return referenced.Concat(federation).ToHashSet(StringComparer.Ordinal);
    }
}
