using Microsoft.EntityFrameworkCore;
using PolvorApp.ArquebusierRegistry.Persistence;
using PolvorApp.FederationCatalog.Contracts;

namespace PolvorApp.ArquebusierRegistry;

/// <summary>
/// The registry's veto on catalogue deletions (spec: Comparsas and weapon models in use): a comparsa
/// with arquebusiers or a weapon model with owned weapons is in use. The cross-schema foreign keys
/// back this up when a deletion races with a registration (design D3).
/// </summary>
internal sealed class RegistryCatalogUsage(ArquebusierRegistryDbContext db) : ICatalogUsage
{
    public Task<bool> IsComparsaInUseAsync(Guid comparsaId, CancellationToken cancellationToken) =>
        db.Arquebusiers.AnyAsync(a => a.ComparsaId == comparsaId, cancellationToken);

    public Task<bool> IsWeaponModelInUseAsync(Guid weaponModelId, CancellationToken cancellationToken) =>
        db.OwnedWeapons.AnyAsync(w => w.WeaponModelId == weaponModelId, cancellationToken);
}
