using Microsoft.EntityFrameworkCore;
using PolvorApp.ArquebusierRegistry.Persistence;
using PolvorApp.SharedKernel.Storage;

namespace PolvorApp.ArquebusierRegistry.Photos;

/// <summary>Tells the platform's orphan sweep which stored photos the registry still references (design D2).</summary>
internal sealed class RegistryPhotoOwner(ArquebusierRegistryDbContext db) : IStoredObjectOwner
{
    public string Prefix => PhotoStorage.Prefix;

    public async Task<IReadOnlySet<string>> FilterReferencedAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken)
    {
        var referenced = await db.Photos.AsNoTracking()
            .Where(p => keys.Contains(p.ObjectKey))
            .Select(p => p.ObjectKey)
            .ToListAsync(cancellationToken);
        return referenced.ToHashSet(StringComparer.Ordinal);
    }
}
