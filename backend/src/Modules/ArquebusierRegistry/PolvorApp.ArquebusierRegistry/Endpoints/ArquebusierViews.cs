using Microsoft.EntityFrameworkCore;
using PolvorApp.ArquebusierRegistry.Arquebusiers;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ArquebusierRegistry.Insights;
using PolvorApp.ArquebusierRegistry.Persistence;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.ComplianceInsights.Contracts;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.FestivalEditions.Contracts;
using PolvorApp.SharedKernel.Time;

namespace PolvorApp.ArquebusierRegistry.Endpoints;

/// <summary>
/// Builds API responses: the catalog names and models come from <see cref="ICatalogDirectory"/>
/// (design D2); the license status, the age and the compliance warnings are derived from today in
/// Europe/Madrid. The first-year flag and the deletion impact come from the comparsa orders
/// (<see cref="IParticipationHistory"/>, add-comparsa-orders design D5).
/// </summary>
internal sealed class ArquebusierViews(
    ArquebusierRegistryDbContext db,
    ICatalogDirectory catalog,
    IComplianceRules rules,
    IEditionDirectory editions,
    IParticipationHistory participation,
    TimeProvider time)
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
        var photos = (await db.Photos.AsNoTracking().Where(p => p.ArquebusierId == arquebusier.Id).ToListAsync(cancellationToken))
            .ToDictionary(p => p.Kind, ArquebusierPhotoResponse.From);
        var current = await editions.GetCurrentAsync(cancellationToken);
        var firstYear = current is null ? null
            : (await participation.FirstYearAsync(current.Year, [arquebusier.Id], cancellationToken)).Of(arquebusier.Id);
        var impact = await participation.GetDeletionImpactAsync(arquebusier.Id, [.. weapons.Select(w => w.Id)], cancellationToken);

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
            new ArquebusierPhotosResponse(
                photos.GetValueOrDefault(ArquebusierPhotoKind.Id),
                photos.GetValueOrDefault(ArquebusierPhotoKind.LicenseFront),
                photos.GetValueOrDefault(ArquebusierPhotoKind.LicenseBack)),
            arquebusier.Version,
            rules.AgeOn(arquebusier.BirthDate, today),
            rules.Evaluate(
                RegistryCompliance.FactsOf(
                    arquebusier.Id,
                    arquebusier.BirthDate,
                    arquebusier.TrainingCompletedOn,
                    new LicenseColumns(arquebusier.LicenseType, arquebusier.LicensePending, arquebusier.LicenseExpiresOn),
                    new PhotoFlags(
                        photos.ContainsKey(ArquebusierPhotoKind.Id),
                        photos.ContainsKey(ArquebusierPhotoKind.LicenseFront),
                        photos.ContainsKey(ArquebusierPhotoKind.LicenseBack))),
                today),
            firstYear,
            await DeletionImpactAsync(impact, cancellationToken));
    }

    private async Task<DeletionImpactResponse> DeletionImpactAsync(DeletionImpact impact, CancellationToken cancellationToken)
    {
        if (impact.CurrentEntry is not { } entry)
        {
            return new DeletionImpactResponse(null, impact.LentWeaponsInCurrentEdition, impact.HasPastEntries);
        }

        // After a transfer the entry may sit in the previous comparsa's order (BR-13): its name is shown.
        var comparsa = await catalog.FindComparsaAsync(entry.ComparsaId, cancellationToken)
            ?? throw new InvalidOperationException($"Comparsa {entry.ComparsaId} of an order is missing from the catalog.");
        return new DeletionImpactResponse(
            new DeletionEntryResponse(entry.EditionYear, comparsa.Id, comparsa.Name, entry.OrderStatus, entry.WillBeRemoved),
            impact.LentWeaponsInCurrentEdition,
            impact.HasPastEntries);
    }
}
