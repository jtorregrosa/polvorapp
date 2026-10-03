using Microsoft.EntityFrameworkCore;
using PolvorApp.ArquebusierRegistry.Arquebusiers;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ArquebusierRegistry.Persistence;
using PolvorApp.SharedKernel.Text;

namespace PolvorApp.ArquebusierRegistry.Insights;

/// <summary>
/// The registry side of <see cref="IArquebusierRoster"/> (add-comparsa-orders, design D5): one
/// projection of the arquebusiers with a flag per photo kind, and one query for their owned weapons.
/// Contact data and the gender are never selected.
/// </summary>
internal sealed class RegistryArquebusierRoster(ArquebusierRegistryDbContext db) : IArquebusierRoster
{
    public async Task<IReadOnlyList<RosterArquebusier>> ListByComparsaAsync(Guid comparsaId, CancellationToken cancellationToken)
    {
        var roster = await ReadAsync(db.Arquebusiers.AsNoTracking().Where(a => a.ComparsaId == comparsaId), cancellationToken);
        return [.. roster
            .OrderBy(a => a.LastName, SpanishOrder.Names)
            .ThenBy(a => a.FirstName, SpanishOrder.Names)
            .ThenBy(a => a.Id)];
    }

    public async Task<IReadOnlyList<RosterArquebusier>> FindManyAsync(IReadOnlyCollection<Guid> arquebusierIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arquebusierIds);
        if (arquebusierIds.Count == 0)
        {
            return [];
        }

        // Copied to an array, so Npgsql sends one uuid[] parameter whatever collection the caller holds.
        Guid[] ids = [.. arquebusierIds];
        return await ReadAsync(db.Arquebusiers.AsNoTracking().Where(a => ids.Contains(a.Id)), cancellationToken);
    }

    public async Task<LenderSummary?> FindLenderAsync(string normalisedNationalId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(normalisedNationalId);
        var lender = await db.Arquebusiers.AsNoTracking()
            .Where(a => a.NationalId == normalisedNationalId)
            .Select(a => new { a.Id, a.FirstName, a.LastName, a.NationalId, a.ComparsaId })
            .SingleOrDefaultAsync(cancellationToken);
        if (lender is null)
        {
            return null;
        }

        var weapons = await WeaponsOfAsync([lender.Id], cancellationToken);
        return new LenderSummary(
            lender.Id,
            lender.FirstName,
            lender.LastName,
            lender.NationalId,
            lender.ComparsaId,
            [.. weapons[lender.Id].Select(w => new LenderWeapon(w.Id, w.WeaponModelId, w.WeaponNumber))]);
    }

    public async Task<IReadOnlyList<RosterWeapon>> FindOwnedWeaponsAsync(IReadOnlyCollection<Guid> ownedWeaponIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ownedWeaponIds);
        if (ownedWeaponIds.Count == 0)
        {
            return [];
        }

        Guid[] ids = [.. ownedWeaponIds];
        return await db.OwnedWeapons.AsNoTracking()
            .Where(w => ids.Contains(w.Id))
            .Select(w => new RosterWeapon(w.Id, w.ArquebusierId, w.WeaponModelId, w.WeaponNumber, w.OwnershipGuideNumber))
            .ToListAsync(cancellationToken);
    }

    public Task<bool> IsNationalIdRegisteredAsync(string normalisedNationalId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(normalisedNationalId);
        return db.Arquebusiers.AnyAsync(a => a.NationalId == normalisedNationalId, cancellationToken);
    }

    private async Task<List<RosterArquebusier>> ReadAsync(IQueryable<Arquebusier> arquebusiers, CancellationToken cancellationToken)
    {
        var rows = await arquebusiers
            .Select(a => new
            {
                a.Id,
                a.ComparsaId,
                a.FirstName,
                a.LastName,
                a.NationalId,
                a.FederationId,
                a.Status,
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
        var weapons = await WeaponsOfAsync([.. rows.Select(a => a.Id)], cancellationToken);

        return [.. rows.Select(a => new RosterArquebusier(
            a.Id,
            a.ComparsaId,
            a.FirstName,
            a.LastName,
            a.NationalId,
            a.FederationId,
            a.Status,
            a.BirthDate,
            RegistryCompliance.LicenseOf(
                a.Id,
                new LicenseColumns(a.LicenseType, a.LicensePending, a.LicenseExpiresOn),
                new PhotoFlags(a.HasIdPhoto, a.HasFrontPhoto, a.HasBackPhoto)),
            a.TrainingCompletedOn,
            a.HasIdPhoto,
            [.. weapons[a.Id]]))];
    }

    private async Task<ILookup<Guid, RosterWeapon>> WeaponsOfAsync(Guid[] ownerIds, CancellationToken cancellationToken) =>
        ownerIds.Length == 0
            ? Array.Empty<RosterWeapon>().ToLookup(w => w.OwnerId)
            : (await db.OwnedWeapons.AsNoTracking()
                .Where(w => ownerIds.Contains(w.ArquebusierId))
                .OrderBy(w => w.CreatedAt)
                .Select(w => new RosterWeapon(w.Id, w.ArquebusierId, w.WeaponModelId, w.WeaponNumber, w.OwnershipGuideNumber))
                .ToListAsync(cancellationToken))
                .ToLookup(w => w.OwnerId);
}
