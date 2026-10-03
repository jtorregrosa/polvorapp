using Microsoft.EntityFrameworkCore;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ArquebusierRegistry.Persistence;

namespace PolvorApp.ArquebusierRegistry.Insights;

/// <summary>
/// The registry side of <see cref="IArquebusierFacts"/> (design D3): one projection of the
/// arquebusiers with a flag per photo kind, and one query for their owned weapons' models. Names,
/// national and federation IDs and contact data are never selected.
/// </summary>
internal sealed class RegistryArquebusierFacts(ArquebusierRegistryDbContext db) : IArquebusierFacts
{
    public Task<IReadOnlyList<ArquebusierFacts>> ListAllAsync(CancellationToken cancellationToken) =>
        ReadAsync(null, cancellationToken);

    public Task<IReadOnlyList<ArquebusierFacts>> ListAsync(IReadOnlyCollection<Guid> comparsaIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(comparsaIds);

        // Copied to an array, so Npgsql sends one uuid[] parameter whatever collection the caller holds.
        return comparsaIds.Count == 0
            ? Task.FromResult<IReadOnlyList<ArquebusierFacts>>([])
            : ReadAsync([.. comparsaIds], cancellationToken);
    }

    private async Task<IReadOnlyList<ArquebusierFacts>> ReadAsync(Guid[]? comparsaIds, CancellationToken cancellationToken)
    {
        var arquebusiers = db.Arquebusiers.AsNoTracking();
        var weapons = db.OwnedWeapons.AsNoTracking();
        if (comparsaIds is not null)
        {
            arquebusiers = arquebusiers.Where(a => comparsaIds.Contains(a.ComparsaId));
            weapons = weapons.Where(w => db.Arquebusiers.Any(a => a.Id == w.ArquebusierId && comparsaIds.Contains(a.ComparsaId)));
        }

        var rows = await arquebusiers
            .Select(a => new
            {
                a.Id,
                a.ComparsaId,
                a.Status,
                a.Gender,
                a.BirthDate,
                a.LicenseType,
                a.LicensePending,
                a.LicenseExpiresOn,
                a.TrainingCompletedOn,
                HasIdPhoto = db.Photos.Any(p => p.ArquebusierId == a.Id && p.Kind == ArquebusierPhotoKind.Id),
                HasFrontPhoto = db.Photos.Any(p => p.ArquebusierId == a.Id && p.Kind == ArquebusierPhotoKind.LicenseFront),
                HasBackPhoto = db.Photos.Any(p => p.ArquebusierId == a.Id && p.Kind == ArquebusierPhotoKind.LicenseBack),
            })
            .ToListAsync(cancellationToken);
        var models = (await weapons.Select(w => new { w.ArquebusierId, w.WeaponModelId }).ToListAsync(cancellationToken))
            .ToLookup(w => w.ArquebusierId, w => w.WeaponModelId);

        return [.. rows.Select(a => new ArquebusierFacts(
            ArquebusierId: a.Id,
            ComparsaId: a.ComparsaId,
            Status: a.Status,
            Gender: a.Gender,
            BirthDate: a.BirthDate,
            License: RegistryCompliance.LicenseOf(
                a.Id,
                new LicenseColumns(a.LicenseType, a.LicensePending, a.LicenseExpiresOn),
                new PhotoFlags(a.HasIdPhoto, a.HasFrontPhoto, a.HasBackPhoto)),
            TrainingCompletedOn: a.TrainingCompletedOn,
            HasIdPhoto: a.HasIdPhoto,
            OwnedWeaponModelIds: [.. models[a.Id]]))];
    }
}
