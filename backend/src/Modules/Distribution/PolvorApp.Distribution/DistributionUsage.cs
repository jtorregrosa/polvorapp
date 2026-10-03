using Microsoft.EntityFrameworkCore;
using PolvorApp.Distribution.Persistence;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.FestivalEditions.Contracts;

namespace PolvorApp.Distribution;

/// <summary>
/// The distribution's veto on edition deletions (spec festival-editions: Edition management by Admins;
/// design D1): a draft edition with a distribution day or a pickup proxy is in use. The foreign keys back
/// this up.
/// </summary>
internal sealed class DistributionEditionUsage(DistributionDbContext db) : IEditionUsage
{
    public async Task<bool> IsEditionInUseAsync(Guid editionId, CancellationToken cancellationToken) =>
        await db.Days.AnyAsync(d => d.EditionId == editionId, cancellationToken)
        || await db.Proxies.AnyAsync(p => p.EditionId == editionId, cancellationToken);
}

/// <summary>
/// The distribution's veto on catalogue deletions (spec: Distribution slots; design D1): a comparsa with
/// a slot or a pickup proxy is in use. Weapon models are never referenced here. The foreign keys back
/// this up.
/// </summary>
internal sealed class DistributionCatalogUsage(DistributionDbContext db) : ICatalogUsage
{
    public async Task<bool> IsComparsaInUseAsync(Guid comparsaId, CancellationToken cancellationToken) =>
        await db.Slots.AnyAsync(s => s.ComparsaId == comparsaId, cancellationToken)
        || await db.Proxies.AnyAsync(p => p.ComparsaId == comparsaId, cancellationToken);

    public Task<bool> IsWeaponModelInUseAsync(Guid weaponModelId, CancellationToken cancellationToken) => Task.FromResult(false);
}
