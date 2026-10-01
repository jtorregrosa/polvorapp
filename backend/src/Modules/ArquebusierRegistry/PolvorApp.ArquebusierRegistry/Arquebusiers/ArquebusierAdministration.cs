using Microsoft.EntityFrameworkCore;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ArquebusierRegistry.Persistence;
using PolvorApp.ArquebusierRegistry.Photos;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.SharedKernel.Auditing;
using PolvorApp.SharedKernel.Codes;

namespace PolvorApp.ArquebusierRegistry.Arquebusiers;

/// <summary>
/// Writes to arquebusiers (specs: Registering and editing arquebusiers, Federation-wide uniqueness,
/// Transfer, Deleting; design D5, D10). Every write is scoped to the caller's comparsas (BR-12),
/// audited in the same transaction without personal values (D7), and backed by the database
/// constraints when it races with another request.
/// </summary>
internal sealed class ArquebusierAdministration(
    ArquebusierRegistryDbContext db,
    IComparsaScope scope,
    ICatalogDirectory catalog,
    IAuditTrail trail,
    TimeProvider time,
    RegistryWriteGuard guard,
    PhotoObjects photoObjects)
{
    public const string EntityType = "Arquebusier";

    public Task<(RegistryOutcome Outcome, Arquebusier? Arquebusier)> RegisterAsync(
        Guid comparsaId, ArquebusierInput input, CancellationToken cancellationToken) =>
        guard.RunAsync<Arquebusier>(nameof(RegisterAsync), null, null, async () =>
        {
            var access = await scope.GetAccessAsync(cancellationToken);
            if (!access.CanAccess(comparsaId))
            {
                return (RegistryOutcome.ComparsaNotFound, null);
            }

            var comparsa = await catalog.FindComparsaAsync(comparsaId, cancellationToken);
            if (comparsa is null)
            {
                return (RegistryOutcome.ComparsaNotFound, null);
            }

            if (!comparsa.Active)
            {
                return (RegistryOutcome.ComparsaInactive, null);
            }

            if (await DuplicateOfAsync(input, exceptId: null, cancellationToken) is { } duplicate)
            {
                return (duplicate, null);
            }

            var now = time.GetUtcNow();
            var arquebusier = new Arquebusier
            {
                Id = Guid.CreateVersion7(now),
                ComparsaId = comparsaId,
                FederationId = input.FederationId,
                NationalId = input.NationalId,
                FirstName = input.FirstName,
                LastName = input.LastName,
                BirthDate = input.BirthDate,
                Gender = input.Gender,
                CreatedAt = now,
            };
            Apply(arquebusier, input);
            db.Arquebusiers.Add(arquebusier);
            Record("ArquebusierRegistered", arquebusier);

            await using var transaction = await db.BeginWriteAsync(cancellationToken);
            var outcome = await SaveAsync(arquebusier.Id, versioned: false, cancellationToken);
            if (outcome != RegistryOutcome.Done)
            {
                return (outcome, null);
            }

            await transaction.CommitAsync(cancellationToken);
            return (RegistryOutcome.Done, arquebusier);
        });

    /// <summary>
    /// Replaces the editable fields of an arquebusier in the caller's scope, if <paramref name="version"/>
    /// is still current. An edit that changes nothing saves and audits nothing (spec: Registering and editing).
    /// </summary>
    public async Task<(RegistryOutcome Outcome, Arquebusier? Arquebusier)> UpdateAsync(
        Guid id, ArquebusierInput input, uint version, CancellationToken cancellationToken)
    {
        List<string> erasedImages = [];
        var result = await guard.RunAsync<Arquebusier>(nameof(UpdateAsync), id, null, async () =>
        {
            // Resolved before the transaction, so no row lock is held while it queries the scope.
            var access = await scope.GetAccessAsync(cancellationToken);
            await using var transaction = await db.BeginWriteAsync(cancellationToken);
            if (!await db.LockArquebusierForChangeAsync(id, access, cancellationToken))
            {
                return (RegistryOutcome.ArquebusierNotFound, null);
            }

            var arquebusier = await db.Arquebusiers.SingleAsync(a => a.Id == id, cancellationToken);
            if (arquebusier.Version != version)
            {
                return (RegistryOutcome.ArquebusierModified, null);
            }

            var changedFields = ChangedFields(arquebusier, input);
            if (changedFields.Count == 0)
            {
                return (RegistryOutcome.Done, arquebusier);
            }

            if (await DuplicateOfAsync(input, exceptId: id, cancellationToken) is { } duplicate)
            {
                return (duplicate, null);
            }

            // Removing the license removes its photos in the same change; a renewal keeps them (spec: Current license).
            var licensePhotos = arquebusier.LicenseType is not null && input.License is null
                ? await db.Photos.Where(p => p.ArquebusierId == id && p.Kind != ArquebusierPhotoKind.Id).ToListAsync(cancellationToken)
                : [];
            db.Photos.RemoveRange(licensePhotos);
            Apply(arquebusier, input);
            Record("ArquebusierUpdated", arquebusier, licensePhotos.Count == 0
                ? new { changedFields }
                : (object)new { changedFields, removedPhotos = licensePhotos.Select(p => EnumCodes.ToCode(p.Kind)).Order(StringComparer.Ordinal).ToList() });
            var outcome = await SaveAsync(id, versioned: true, cancellationToken);
            if (outcome != RegistryOutcome.Done)
            {
                return (outcome, null);
            }

            await transaction.CommitAsync(cancellationToken);
            erasedImages = [.. licensePhotos.Select(p => p.ObjectKey)];
            return (RegistryOutcome.Done, arquebusier);
        });

        await photoObjects.DeleteAsync(erasedImages);
        return result;
    }

    /// <summary>
    /// Moves an arquebusier, with their owned weapons, to another active comparsa (UC-29, BR-13).
    /// Admin-only at the endpoint. The row is locked <c>FOR UPDATE</c>, so no edit or weapon write of
    /// the previous comparsa's FiringChiefs lands after the move; the foreign key decides a race with
    /// the deletion of the target.
    /// </summary>
    public Task<(RegistryOutcome Outcome, Arquebusier? Arquebusier)> TransferAsync(
        Guid id, Guid targetComparsaId, CancellationToken cancellationToken) =>
        guard.RunAsync<Arquebusier>(nameof(TransferAsync), id, null, async () =>
        {
            var access = await scope.GetAccessAsync(cancellationToken);
            var target = await catalog.FindComparsaAsync(targetComparsaId, cancellationToken);
            await using var transaction = await db.BeginWriteAsync(cancellationToken);
            if (!await db.LockArquebusierForUpdateAsync(id, access, cancellationToken))
            {
                return (RegistryOutcome.ArquebusierNotFound, null);
            }

            var arquebusier = await db.Arquebusiers.SingleAsync(a => a.Id == id, cancellationToken);
            if (arquebusier.ComparsaId == targetComparsaId)
            {
                return (RegistryOutcome.SameComparsa, null);
            }

            if (target is null || !access.CanAccess(targetComparsaId))
            {
                return (RegistryOutcome.ComparsaNotFound, null);
            }

            if (!target.Active)
            {
                return (RegistryOutcome.ComparsaInactive, null);
            }

            // Recorded under the previous comparsa, with both ids, before the move.
            Record("ArquebusierTransferred", arquebusier, new { fromComparsaId = arquebusier.ComparsaId, toComparsaId = targetComparsaId });
            arquebusier.ComparsaId = targetComparsaId;
            var outcome = await SaveAsync(id, versioned: true, cancellationToken);
            if (outcome != RegistryOutcome.Done)
            {
                return (outcome, null);
            }

            await transaction.CommitAsync(cancellationToken);
            return (RegistryOutcome.Done, arquebusier);
        });

    /// <summary>
    /// Deletes an arquebusier who left the Federation, with their owned weapons and photos (UC-05,
    /// BR-14). The audit entry keeps only the counts of weapons and photos removed, so no personal data
    /// remains. #10 extends this with the anonymisation of past edition entries.
    /// </summary>
    public async Task<(RegistryOutcome Outcome, Arquebusier? Arquebusier)> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        List<string> erasedImages = [];
        var result = await guard.RunAsync<Arquebusier>(nameof(DeleteAsync), id, null, async () =>
        {
            var access = await scope.GetAccessAsync(cancellationToken);
            await using var transaction = await db.BeginWriteAsync(cancellationToken);
            if (!await db.LockArquebusierForUpdateAsync(id, access, cancellationToken))
            {
                return (RegistryOutcome.ArquebusierNotFound, null);
            }

            var arquebusier = await db.Arquebusiers.SingleAsync(a => a.Id == id, cancellationToken);
            var ownedWeaponCount = await db.OwnedWeapons.CountAsync(w => w.ArquebusierId == id, cancellationToken);
            var photoKeys = await db.Photos.Where(p => p.ArquebusierId == id).Select(p => p.ObjectKey).ToListAsync(cancellationToken);

            // The owned weapons and the photo references go with the row (ON DELETE CASCADE).
            db.Arquebusiers.Remove(arquebusier);
            Record("ArquebusierDeleted", arquebusier, new { ownedWeaponCount, photoCount = photoKeys.Count });
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            erasedImages = photoKeys;
            return (RegistryOutcome.Done, arquebusier);
        });

        // The images go after the commit (BR-14); a failure is left to the orphan sweep (design D2).
        await photoObjects.DeleteAsync(erasedImages);
        return result;
    }

    /// <summary>The names of the fields an edit changes, in a stable order; the values are never recorded (D7).</summary>
    private static List<string> ChangedFields(Arquebusier current, ArquebusierInput input)
    {
        var changes = new (string Field, bool Changed)[]
        {
            ("federationId", current.FederationId != input.FederationId),
            ("nationalId", current.NationalId != input.NationalId),
            ("firstName", current.FirstName != input.FirstName),
            ("lastName", current.LastName != input.LastName),
            ("birthDate", current.BirthDate != input.BirthDate),
            ("email", current.Email != input.Email),
            ("phone", current.Phone != input.Phone),
            ("gender", current.Gender != input.Gender),
            ("status", current.Status != input.Status),
            ("trainingCompletedOn", current.TrainingCompletedOn != input.TrainingCompletedOn),
            ("license", current.CurrentLicense() != input.License),
        };
        return [.. changes.Where(c => c.Changed).Select(c => c.Field)];
    }

    /// <summary>Copies the editable fields; the comparsa only changes through a transfer.</summary>
    private static void Apply(Arquebusier arquebusier, ArquebusierInput input)
    {
        arquebusier.FederationId = input.FederationId;
        arquebusier.NationalId = input.NationalId;
        arquebusier.FirstName = input.FirstName;
        arquebusier.LastName = input.LastName;
        arquebusier.BirthDate = input.BirthDate;
        arquebusier.Email = input.Email;
        arquebusier.Phone = input.Phone;
        arquebusier.Gender = input.Gender;
        arquebusier.Status = input.Status;
        arquebusier.TrainingCompletedOn = input.TrainingCompletedOn;
        arquebusier.LicenseType = input.License?.Type;
        arquebusier.LicensePending = input.License?.Pending ?? false;
        arquebusier.LicenseIssuedOn = input.License?.IssuedOn;
        arquebusier.LicenseExpiresOn = input.License?.ExpiresOn;
    }

    /// <summary>
    /// BR-02 pre-check, so the common case gets a clear answer; the unique indexes decide races.
    /// It looks across every comparsa but returns only which value is taken, never whose.
    /// </summary>
    private async Task<RegistryOutcome?> DuplicateOfAsync(ArquebusierInput input, Guid? exceptId, CancellationToken cancellationToken)
    {
        var others = db.Arquebusiers.AsNoTracking().Where(a => a.Id != exceptId);
        if (await others.AnyAsync(a => a.NationalId == input.NationalId, cancellationToken))
        {
            return RegistryOutcome.NationalIdTaken;
        }

        return await others.AnyAsync(a => a.FederationId == input.FederationId, cancellationToken)
            ? RegistryOutcome.FederationIdTaken
            : null;
    }

    /// <summary>
    /// Saves the change with its audit entry, mapping a lost race to its blocking outcome (design D5).
    /// Only an edit of a row loaded with its version can raise a concurrency conflict.
    /// </summary>
    private async Task<RegistryOutcome> SaveAsync(Guid arquebusierId, bool versioned, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return RegistryOutcome.Done;
        }
        catch (DbUpdateConcurrencyException) when (versioned)
        {
            db.ChangeTracker.Clear();
            return RegistryOutcome.ArquebusierModified;
        }
        catch (DbUpdateException exception) when (RegistryProblems.ViolatedConstraint(exception) is { } constraint
            && OutcomeOf(constraint) is { } outcome)
        {
            db.ChangeTracker.Clear();
            guard.LostRace(arquebusierId, constraint);
            return outcome;
        }
    }

    private static RegistryOutcome? OutcomeOf(string constraint) => constraint switch
    {
        ArquebusierRegistryDbContext.NationalIdIndex => RegistryOutcome.NationalIdTaken,
        ArquebusierRegistryDbContext.FederationIdIndex => RegistryOutcome.FederationIdTaken,

        // The comparsa was deleted after the catalog lookup (design D10, locking rules).
        ArquebusierRegistryDbContext.ComparsaForeignKey => RegistryOutcome.ComparsaNotFound,
        _ => null,
    };

    /// <summary>Audit entries name what changed, never personal values (design D7).</summary>
    private void Record(string action, Arquebusier arquebusier, object? data = null) =>
        trail.Record(db, new AuditRecord(action, EntityType, arquebusier.Id.ToString(), data, ComparsaId: arquebusier.ComparsaId));
}
