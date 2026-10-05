using Microsoft.EntityFrameworkCore;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.FestivalEditions.Persistence;

namespace PolvorApp.FestivalEditions;

/// <summary>
/// The editions' veto on catalogue deletions (spec: Rental models offered in an edition): a
/// weapon model in any edition's set is in use. Editions reference no comparsa. The cross-schema
/// foreign key backs this up when a deletion races with a change of the set (design D1, D7).
/// </summary>
internal sealed class FestivalEditionsCatalogUsage(FestivalEditionsDbContext db) : ICatalogUsage
{
    public Task<bool> IsComparsaInUseAsync(Guid comparsaId, CancellationToken cancellationToken) => Task.FromResult(false);

    public Task<bool> IsWeaponModelInUseAsync(Guid weaponModelId, CancellationToken cancellationToken) =>
        db.EditionWeaponModels.AnyAsync(m => m.WeaponModelId == weaponModelId, cancellationToken);
}
