using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PolvorApp.ArquebusierRegistry.Arquebusiers;
using PolvorApp.ArquebusierRegistry.Persistence;
using PolvorApp.AuditPrivacy.Contracts;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.SharedKernel.Codes;
using PolvorApp.SharedKernel.Persistence;
using PolvorApp.SharedKernel.Storage;

namespace PolvorApp.ArquebusierRegistry.Privacy;

/// <summary>
/// The registry's part of a GDPR request about a person (UC-26; add-audit-privacy, design D5, D6): the
/// arquebusier with that DNI/NIE, their owned weapons and photos. The erasure is the registry deletion
/// (BR-14) on the request's transaction, so the deletion participants (the current-edition entry while
/// the orders are open) act exactly as for a deletion.
/// </summary>
internal sealed partial class RegistryPersonalData(
    ArquebusierRegistryDbContext db,
    ArquebusierAdministration administration,
    ICatalogDirectory catalog,
    IObjectStorage storage,
    ILogger<RegistryPersonalData> logger) : IPersonalDataParticipant
{
    public const string RegistrySheet = "registry";
    public const string WeaponsSheet = "ownedWeapons";

    /// <summary>The note of a photo whose stored image is missing, followed by its kind (e.g. <c>missingPhoto:id</c>).</summary>
    public const string MissingPhotoNote = "missingPhoto";

    public int Order => PersonalDataParticipantOrder.Registry;

    public async Task<PersonalDataSummary> DescribeAsync(PersonalDataSubject subject, CancellationToken cancellationToken)
    {
        if (subject is not PersonalDataSubject.Person(var nationalId))
        {
            return PersonalDataSummary.Empty;
        }

        var record = await db.Arquebusiers.AsNoTracking()
            .Where(a => a.NationalId == nationalId)
            .Select(a => new RegistryRecordSummary(
                a.Id,
                a.FirstName,
                a.LastName,
                a.ComparsaId,
                EnumCodes.ToCode(a.Status),
                db.OwnedWeapons.Count(w => w.ArquebusierId == a.Id),
                db.Photos.Count(p => p.ArquebusierId == a.Id)))
            .SingleOrDefaultAsync(cancellationToken);
        return record is null ? PersonalDataSummary.Empty : new PersonalDataSummary { Registry = record };
    }

    public async Task<PersonalDataExportPart> ExportAsync(PersonalDataSubject subject, CancellationToken cancellationToken)
    {
        if (subject is not PersonalDataSubject.Person(var nationalId))
        {
            return PersonalDataExportPart.Empty;
        }

        var arquebusier = await db.Arquebusiers.AsNoTracking().SingleOrDefaultAsync(a => a.NationalId == nationalId, cancellationToken);
        if (arquebusier is null)
        {
            return PersonalDataExportPart.Empty;
        }

        var weapons = await db.OwnedWeapons.AsNoTracking().Where(w => w.ArquebusierId == arquebusier.Id).OrderBy(w => w.WeaponNumber).ToListAsync(cancellationToken);
        var photos = await db.Photos.AsNoTracking().Where(p => p.ArquebusierId == arquebusier.Id).OrderBy(p => p.Kind).ToListAsync(cancellationToken);
        var comparsa = await catalog.FindComparsaAsync(arquebusier.ComparsaId, cancellationToken);
        var models = (await catalog.FindWeaponModelsAsync([.. weapons.Select(w => w.WeaponModelId).Distinct()], cancellationToken))
            .ToDictionary(m => m.Id, m => m.Label);

        PersonalDataSheet registry = new(
            RegistrySheet,
            ["nationalId", "federationId", "firstName", "lastName", "birthDate", "email", "phone", "gender", "status", "comparsa",
             "trainingCompletedOn", "licenseType", "licensePending", "licenseIssuedOn", "licenseExpiresOn", "registeredAt"],
            [[
                arquebusier.NationalId, arquebusier.FederationId, arquebusier.FirstName, arquebusier.LastName, arquebusier.BirthDate,
                arquebusier.Email, arquebusier.Phone, EnumCodes.ToCode(arquebusier.Gender), EnumCodes.ToCode(arquebusier.Status), comparsa?.Name,
                arquebusier.TrainingCompletedOn, arquebusier.LicenseType is { } type ? EnumCodes.ToCode(type) : null, arquebusier.LicensePending,
                arquebusier.LicenseIssuedOn, arquebusier.LicenseExpiresOn, arquebusier.CreatedAt,
            ]]);
        List<PersonalDataSheet> sheets = [registry];
        if (weapons.Count > 0)
        {
            sheets.Add(new PersonalDataSheet(
                WeaponsSheet,
                ["weaponModel", "weaponNumber", "ownershipGuideNumber"],
                [.. weapons.Select(w => (IReadOnlyList<object?>)[models.GetValueOrDefault(w.WeaponModelId), w.WeaponNumber, w.OwnershipGuideNumber])]));
        }

        var files = new List<PersonalDataFile>();
        var notes = new List<string>();
        foreach (var photo in photos)
        {
            var kind = EnumCodes.ToCode(photo.Kind).ToLowerInvariant().Replace('_', '-');
            await using var stored = await storage.GetAsync(photo.ObjectKey, cancellationToken);
            if (stored is null)
            {
                // A record without its image: the export says so instead of omitting it silently.
                LogMissingPhoto(logger, photo.Id, kind);
                notes.Add($"{MissingPhotoNote}:{kind}");
                continue;
            }

            using var buffer = new MemoryStream();
            try
            {
                await stored.Content.CopyToAsync(buffer, cancellationToken);
            }
            catch (Exception exception) when (exception is IOException or HttpRequestException)
            {
                // The storage failed while sending the image: the export fails as a whole (503), never without it.
                throw new StorageUnavailableException("A stored photo could not be read.", exception);
            }

            files.Add(new PersonalDataFile($"photos/{kind}.jpg", stored.ContentType, buffer.ToArray()));
        }

        return new PersonalDataExportPart(sheets, files) { Notes = notes };
    }

    public async Task PrepareErasureAsync(
        PersonalDataSubject subject, PersonalDataErasure erasure, DbTransaction transaction, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(erasure);
        if (subject is not PersonalDataSubject.Person(var nationalId))
        {
            return;
        }

        await db.EnlistAsync(transaction, cancellationToken);
        var id = await db.Arquebusiers.Where(a => a.NationalId == nationalId).Select(a => (Guid?)a.Id).SingleOrDefaultAsync(cancellationToken);
        // Locked first, as a deletion does, so an erasure and a deletion of the same person cannot
        // deadlock; then checked again, in case the DNI/NIE was edited between the read and the lock.
        if (id is { } arquebusierId
            && await db.LockArquebusierForUpdateAsync(arquebusierId, ComparsaAccess.All, cancellationToken)
            && await db.Arquebusiers.AnyAsync(a => a.Id == arquebusierId && a.NationalId == nationalId, cancellationToken))
        {
            erasure.ArquebusierId = arquebusierId;
            erasure.AddOwnedWeapons(await db.OwnedWeapons.Where(w => w.ArquebusierId == arquebusierId).Select(w => w.Id).ToListAsync(cancellationToken));
        }
    }

    public async Task EraseAsync(
        PersonalDataSubject subject, PersonalDataErasure erasure, DbTransaction transaction, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(erasure);
        if (subject is not PersonalDataSubject.Person || erasure.ArquebusierId is not { } id)
        {
            return;
        }

        var deletion = await administration.DeleteCoreAsync(id, ComparsaAccess.All, transaction, gdprErasure: true, cancellationToken)
            ?? throw new InvalidOperationException("The arquebusier locked for the erasure is gone.");
        erasure.Count("arquebusiersDeleted", 1);
        erasure.Count("ownedWeaponsDeleted", deletion.OwnedWeaponIds.Count);
        erasure.Count("photosDeleted", deletion.PhotoKeys.Count);
        erasure.DeleteObjectsAfterCommit(deletion.PhotoKeys);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Photo {PhotoId} ({Kind}) of a personal data export has no stored image; the export notes it")]
    private static partial void LogMissingPhoto(ILogger logger, Guid photoId, string kind);
}
