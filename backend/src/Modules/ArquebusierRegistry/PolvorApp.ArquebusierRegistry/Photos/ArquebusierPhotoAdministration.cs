using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ArquebusierRegistry.Persistence;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.SharedKernel.Auditing;
using PolvorApp.SharedKernel.Codes;
using PolvorApp.SharedKernel.Images;
using PolvorApp.SharedKernel.Storage;

namespace PolvorApp.ArquebusierRegistry.Photos;

/// <summary>How a photo upload ended: the stored photo, or the outcome and, for an image that broke a rule, which rule.</summary>
internal sealed record PhotoUpload(RegistryOutcome Outcome, ArquebusierPhoto? Photo, ImageRejection? Rejection = null);

/// <summary>
/// Uploads, reads and removes arquebusier photos (spec: Arquebusier photos; design D5, D6). Every
/// operation is scoped to the caller's comparsas (BR-12). An upload stores the normalised image under
/// a new random key, commits the reference with its audit entry, and only then erases the replaced
/// image. Every failure leaves at most an unreferenced image: it is erased at once when the reference
/// was surely not committed, and by the orphan sweep otherwise (design D2).
/// </summary>
internal sealed partial class ArquebusierPhotoAdministration(
    ArquebusierRegistryDbContext db,
    IComparsaScope scope,
    ICurrentUser currentUser,
    IAuditTrail trail,
    IObjectStorage storage,
    IImageNormalizer images,
    PhotoObjects objects,
    PhotoReader reader,
    TimeProvider time,
    RegistryWriteGuard guard,
    ILogger<ArquebusierPhotoAdministration> logger)
{
    /// <summary>
    /// Whether the arquebusier exists in the caller's scope. Uploads ask this before the body is read,
    /// so out-of-scope and unknown ids cost neither the upload nor any image work (spec: Arquebusier photos).
    /// </summary>
    public async Task<bool> ExistsInScopeAsync(Guid arquebusierId, ArquebusierPhotoKind kind, CancellationToken cancellationToken)
    {
        var access = await scope.GetAccessAsync(cancellationToken);
        var exists = await access.Filter(db.Arquebusiers.AsNoTracking(), a => a.ComparsaId).AnyAsync(a => a.Id == arquebusierId, cancellationToken);
        if (!exists)
        {
            LogRejected(logger, nameof(UploadAsync), RegistryOutcome.ArquebusierNotFound.ToString(), arquebusierId, kind, currentUser.UserId);
        }

        return exists;
    }

    /// <summary>Normalises and stores <paramref name="file"/> as the photo of <paramref name="kind"/>, replacing any previous one.</summary>
    public async Task<PhotoUpload> UploadAsync(Guid arquebusierId, ArquebusierPhotoKind kind, Stream file, CancellationToken cancellationToken)
    {
        var access = await scope.GetAccessAsync(cancellationToken);

        // A locked registry refuses a FiringChief's upload before any image work (design D8);
        // the check inside the save transaction stays the authority.
        if (await guard.IsLockedForCallerAsync(cancellationToken))
        {
            return Rejected(new PhotoUpload(RegistryOutcome.RegistryLocked, null), arquebusierId, kind);
        }

        ImageNormalization normalized;
        try
        {
            normalized = await images.NormalizeAsync(file, PhotoStorage.RulesFor(kind), cancellationToken);
        }
        catch (ImageProcessingBusyException)
        {
            return Rejected(new PhotoUpload(RegistryOutcome.Busy, null), arquebusierId, kind);
        }

        if (normalized is RejectedImage rejected)
        {
            return Rejected(new PhotoUpload(RegistryOutcome.ImageRejected, null, rejected.Reason), arquebusierId, kind);
        }

        var image = (NormalizedImage)normalized;
        var now = time.GetUtcNow();
        var id = Guid.CreateVersion7(now);
        var photo = new ArquebusierPhoto
        {
            Id = id,
            ArquebusierId = arquebusierId,
            Kind = kind,
            ObjectKey = ArquebusierPhoto.KeyFor(id),
            Width = image.Width,
            Height = image.Height,
            SizeBytes = image.Content.Length,
            UploadedAt = now,
        };
        try
        {
            await storage.PutAsync(photo.ObjectKey, image.Content, PhotoStorage.ContentType, cancellationToken);
        }
        catch (StorageUnavailableException)
        {
            // The put may have landed: the sweep erases it if so.
            return Rejected(new PhotoUpload(RegistryOutcome.StorageUnavailable, null), arquebusierId, kind);
        }

        var save = new UploadSave(photo);
        try
        {
            var (outcome, saved) = await guard.RunAsync<ArquebusierPhoto>(nameof(UploadAsync), arquebusierId, null, async () =>
            {
                var result = await SaveUploadAsync(save, access, cancellationToken);
                return (result, result == RegistryOutcome.Done ? photo : null);
            });

            if (outcome != RegistryOutcome.Done && save.CommitAttempted)
            {
                // A retryable failure during or after the commit: it may have succeeded, so the
                // image is left to the sweep rather than erased under a reference (design D2).
                LogLeftForSweep(logger, arquebusierId, photo.Id);
                return new PhotoUpload(outcome, null);
            }

            // After the commit the replaced image is unreferenced; after a rejection, the new one.
            await objects.DeleteAsync(outcome == RegistryOutcome.Done ? save.ReplacedKey : photo.ObjectKey);
            return new PhotoUpload(outcome, saved);
        }
        catch (Exception)
        {
            // Before the commit was attempted, the reference surely does not exist; during or after
            // it, the commit may have succeeded, so the image is left to the sweep (design D2).
            if (save.CommitAttempted)
            {
                LogLeftForSweep(logger, arquebusierId, photo.Id);
            }
            else
            {
                await objects.DeleteAsync(photo.ObjectKey);
            }

            throw;
        }
    }

    /// <summary>The photo and its stored image, opened for reading; the caller disposes the image.</summary>
    public async Task<(RegistryOutcome Outcome, StoredObject? Image)> OpenAsync(Guid arquebusierId, ArquebusierPhotoKind kind, CancellationToken cancellationToken)
    {
        var access = await scope.GetAccessAsync(cancellationToken);
        if (!await access.Filter(db.Arquebusiers.AsNoTracking(), a => a.ComparsaId).AnyAsync(a => a.Id == arquebusierId, cancellationToken))
        {
            return (RegistryOutcome.ArquebusierNotFound, null);
        }

        try
        {
            return await reader.OpenAsync(arquebusierId, kind, cancellationToken) is { } stored
                ? (RegistryOutcome.Done, stored)
                : (RegistryOutcome.PhotoNotFound, null);
        }
        catch (StorageUnavailableException)
        {
            return (RegistryOutcome.StorageUnavailable, null);
        }
    }

    public async Task<RegistryOutcome> RemoveAsync(Guid arquebusierId, ArquebusierPhotoKind kind, CancellationToken cancellationToken)
    {
        var access = await scope.GetAccessAsync(cancellationToken);
        string? removedKey = null;
        var (outcome, _) = await guard.RunAsync<ArquebusierPhoto>(nameof(RemoveAsync), arquebusierId, null, async () =>
        {
            await using var transaction = await guard.BeginWriteAsync(cancellationToken);
            if (!await db.LockArquebusierForShareAsync(arquebusierId, access, cancellationToken))
            {
                return (RegistryOutcome.ArquebusierNotFound, null);
            }

            if (await LockPhotoAsync(arquebusierId, kind, cancellationToken) is not { } photo)
            {
                return (RegistryOutcome.PhotoNotFound, null);
            }

            await db.Photos.Where(p => p.Id == photo.Id).ExecuteDeleteAsync(cancellationToken);
            Record(ArquebusierRegistryAuditActions.ArquebusierPhotoRemoved, arquebusierId, await ComparsaOfAsync(arquebusierId, cancellationToken), new { kind = EnumCodes.ToCode(kind) });
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            removedKey = photo.ObjectKey;
            return (RegistryOutcome.Done, photo);
        });

        await objects.DeleteAsync(removedKey);
        return outcome;
    }

    /// <summary>
    /// Stores the reference in one transaction: locks the arquebusier <c>FOR SHARE</c> in the scope,
    /// checks the license for license photos, replaces the previous photo of the kind in place (the
    /// last of two concurrent replacements wins) or inserts the first one, and audits.
    /// </summary>
    private async Task<RegistryOutcome> SaveUploadAsync(UploadSave save, ComparsaAccess access, CancellationToken cancellationToken)
    {
        var photo = save.Photo;
        await using var transaction = await guard.BeginWriteAsync(cancellationToken);
        if (!await db.LockArquebusierForShareAsync(photo.ArquebusierId, access, cancellationToken))
        {
            return RegistryOutcome.ArquebusierNotFound;
        }

        var owner = await db.Arquebusiers.AsNoTracking()
            .Where(a => a.Id == photo.ArquebusierId)
            .Select(a => new { a.ComparsaId, HasLicense = a.LicenseType != null })
            .SingleAsync(cancellationToken);
        if (PhotoStorage.NeedsLicense(photo.Kind) && !owner.HasLicense)
        {
            return RegistryOutcome.PhotoNeedsLicense;
        }

        // A concurrent replacement waits here and then sees the row the first one wrote.
        var previous = await LockPhotoAsync(photo.ArquebusierId, photo.Kind, cancellationToken);
        if (previous is null)
        {
            db.Photos.Add(photo);
        }
        else
        {
            await db.Database.ExecuteSqlAsync(
                $"UPDATE registry.arquebusier_photos SET id = {photo.Id}, object_key = {photo.ObjectKey}, width = {photo.Width}, height = {photo.Height}, size_bytes = {photo.SizeBytes}, uploaded_at = {photo.UploadedAt} WHERE id = {previous.Id}",
                cancellationToken);
        }

        Record(ArquebusierRegistryAuditActions.ArquebusierPhotoUploaded, photo.ArquebusierId, owner.ComparsaId, new { kind = EnumCodes.ToCode(photo.Kind), replaced = previous is not null });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (RegistryProblems.ViolatedConstraint(exception) is { } constraint
            && constraint is ArquebusierRegistryDbContext.PhotoKindIndex or ArquebusierRegistryDbContext.PhotoArquebusierForeignKey)
        {
            // Two first uploads of the kind raced and this one lost; the foreign key cannot fire under the lock.
            db.ChangeTracker.Clear();
            guard.LostRace(photo.ArquebusierId, constraint);
            return constraint == ArquebusierRegistryDbContext.PhotoKindIndex ? RegistryOutcome.PhotoModified : RegistryOutcome.ArquebusierNotFound;
        }

        save.CommitAttempted = true;
        await transaction.CommitAsync(cancellationToken);
        save.ReplacedKey = previous?.ObjectKey;
        return RegistryOutcome.Done;
    }

    /// <summary>The photo of a kind, locked so that a concurrent replacement erases the right image.</summary>
    private Task<ArquebusierPhoto?> LockPhotoAsync(Guid arquebusierId, ArquebusierPhotoKind kind, CancellationToken cancellationToken)
    {
        var code = EnumCodes.ToCode(kind);
        return db.Photos
            .FromSql($"SELECT id, arquebusier_id, kind, object_key, width, height, size_bytes, uploaded_at FROM registry.arquebusier_photos WHERE arquebusier_id = {arquebusierId} AND kind = {code} FOR UPDATE")
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);
    }

    private Task<Guid> ComparsaOfAsync(Guid arquebusierId, CancellationToken cancellationToken) =>
        db.Arquebusiers.AsNoTracking().Where(a => a.Id == arquebusierId).Select(a => a.ComparsaId).SingleAsync(cancellationToken);

    /// <summary>Audit entries name the kind, never image data, keys or file names (design D8).</summary>
    private void Record(string action, Guid arquebusierId, Guid comparsaId, object data) =>
        trail.Record(db, new AuditRecord(action, Arquebusiers.ArquebusierAdministration.EntityType, arquebusierId.ToString(), data, ComparsaId: comparsaId));

    /// <summary>Rejections are logged with ids, kinds and reasons only, so probing and saturation are visible.</summary>
    private PhotoUpload Rejected(PhotoUpload upload, Guid arquebusierId, ArquebusierPhotoKind kind)
    {
        var reason = upload.Rejection?.ToString() ?? upload.Outcome.ToString();
        LogRejected(logger, nameof(UploadAsync), reason, arquebusierId, kind, currentUser.UserId);
        return upload;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Photo {Operation} rejected with {Reason} for arquebusier {ArquebusierId}, kind {Kind}, by user {UserId}")]
    private static partial void LogRejected(ILogger logger, string operation, string reason, Guid arquebusierId, ArquebusierPhotoKind kind, Guid? userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Upload of photo {PhotoId} for arquebusier {ArquebusierId} failed at the commit; its image is left to the orphan sweep")]
    private static partial void LogLeftForSweep(ILogger logger, Guid arquebusierId, Guid photoId);

    /// <summary>The state of one upload's save, read after the guarded transaction.</summary>
    private sealed class UploadSave(ArquebusierPhoto photo)
    {
        public ArquebusierPhoto Photo { get; } = photo;

        /// <summary>Set just before the commit: from then on the reference may exist.</summary>
        public bool CommitAttempted { get; set; }

        public string? ReplacedKey { get; set; }
    }
}
