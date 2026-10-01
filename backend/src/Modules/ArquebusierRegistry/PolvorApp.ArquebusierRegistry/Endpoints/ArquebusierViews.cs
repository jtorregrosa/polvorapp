using Microsoft.EntityFrameworkCore;
using PolvorApp.ArquebusierRegistry.Arquebusiers;
using PolvorApp.ArquebusierRegistry.Persistence;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.SharedKernel.Time;

namespace PolvorApp.ArquebusierRegistry.Endpoints;

/// <summary>
/// Builds API responses: the catalog names and models come from <see cref="ICatalogDirectory"/>
/// (design D2) and the license status is derived from today in Europe/Madrid.
/// </summary>
internal sealed class ArquebusierViews(ArquebusierRegistryDbContext db, ICatalogDirectory catalog, TimeProvider time)
{
    /// <summary>The detail of <paramref name="arquebusier"/>, with its owned weapons read fresh.</summary>
    public async Task<ArquebusierResponse> DetailAsync(Arquebusier arquebusier, CancellationToken cancellationToken)
    {
        var weapons = await db.OwnedWeapons.AsNoTracking()
            .Where(w => w.ArquebusierId == arquebusier.Id)
            .OrderBy(w => w.CreatedAt)
            .ToListAsync(cancellationToken);
        var comparsa = await catalog.FindComparsaAsync(arquebusier.ComparsaId, cancellationToken)
            ?? throw new InvalidOperationException($"Comparsa {arquebusier.ComparsaId} of arquebusier {arquebusier.Id} is missing from the catalog.");
        var models = (await catalog.FindWeaponModelsAsync([.. weapons.Select(w => w.WeaponModelId).Distinct()], cancellationToken))
            .ToDictionary(m => m.Id);
        var today = FederationCalendar.Today(time);

        return new ArquebusierResponse(
            arquebusier.Id,
            comparsa.Id,
            comparsa.Name,
            comparsa.Active,
            arquebusier.FederationId,
            arquebusier.NationalId,
            arquebusier.FirstName,
            arquebusier.LastName,
            arquebusier.BirthDate,
            arquebusier.Email,
            arquebusier.Phone,
            arquebusier.Gender,
            arquebusier.Status,
            arquebusier.TrainingCompletedOn,
            arquebusier.CurrentLicense() is { } license
                ? new LicenseResponse(license.Type, license.Pending, license.IssuedOn, license.ExpiresOn, license.StatusOn(today))
                : null,
            [.. weapons.Select(w => new OwnedWeaponResponse(
                w.Id,
                models.TryGetValue(w.WeaponModelId, out var model)
                    ? model
                    : throw new InvalidOperationException($"Weapon model {w.WeaponModelId} of owned weapon {w.Id} is missing from the catalog."),
                w.WeaponNumber,
                w.OwnershipGuideNumber,
                w.Version))],
            arquebusier.Version);
    }
}
