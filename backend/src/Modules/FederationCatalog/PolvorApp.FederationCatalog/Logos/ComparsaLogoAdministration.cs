using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PolvorApp.FederationCatalog.Comparsas;
using PolvorApp.FederationCatalog.Persistence;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.SharedKernel.Auditing;
using PolvorApp.SharedKernel.Images;
using PolvorApp.SharedKernel.Storage;

namespace PolvorApp.FederationCatalog.Logos;

/// <summary>How a logo upload ended: the stored logo, or the outcome and, for an image that broke a rule, which rule.</summary>
internal sealed record LogoUpload(CatalogOutcome Outcome, ComparsaLogo? Logo, ImageRejection? Rejection = null);

/// <summary>
/// Uploads, reads and removes comparsa logos (spec: Comparsa logos, Logo access; design D4). Writes
/// are Admin-only at the endpoint; reads are scoped to the caller's comparsas (BR-12). An upload
/// stores the normalised image under a new random key, commits the reference with its audit entry
/// under the comparsa's row lock, and only then erases the replaced image. Every failure leaves at
/// most an unreferenced image: erased at once when the reference was surely not committed, and by
/// the orphan sweep otherwise.
/// </summary>
internal sealed partial class ComparsaLogoAdministration(
    FederationCatalogDbContext db,
    IComparsaScope scope,
    ICurrentUser currentUser,
    IAuditTrail trail,
    IObjectStorage storage,
    IImageNormalizer images,
    LogoObjects objects,
    LogoReader reader,
    TimeProvider time,
    ILogger<ComparsaLogoAdministration> logger)
{
    /// <summary>
    /// Whether the comparsa exists. Uploads ask this before the body is read, so unknown ids cost
    /// neither the upload nor any image work.
    /// </summary>
    public Task<bool> ExistsAsync(Guid comparsaId, CancellationToken cancellationToken) =>
        db.Comparsas.AsNoTracking().AnyAsync(c => c.Id == comparsaId, cancellationToken);

    /// <summary>Normalises and stores <paramref name="file"/> as the comparsa's logo, replacing any previous one.</summary>
    public async Task<LogoUpload> UploadAsync(Guid comparsaId, Stream file, CancellationToken cancellationToken)
    {
        ImageNormalization normalized;
        try
        {
            normalized = await images.NormalizeAsync(file, LogoStorage.Rules, cancellationToken);
        }
        catch (ImageProcessingBusyException)
        {
            return Rejected(new LogoUpload(CatalogOutcome.Busy, null), comparsaId);
        }

        if (normalized is RejectedImage rejected)
        {
            return Rejected(new LogoUpload(CatalogOutcome.ImageRejected, null, rejected.Reason), comparsaId);
        }

        var image = (NormalizedImage)normalized;
        var logo = NewLogo(image);
        var save = new LogoSave();
        try
        {
            await storage.PutAsync(logo.ObjectKey, image.Content, LogoStorage.ContentType, cancellationToken);
            await SaveAsync(comparsaId, logo, save, cancellationToken);
        }
        catch (Exception exception) when (!save.CommitAttempted && (exception is StorageUnavailableException || CatalogLocks.IsRetryable(exception)))
        {
            // Nothing was committed: the new image (if the put landed) is unreferenced.
            db.ChangeTracker.Clear();
            await objects.DeleteAsync(logo.ObjectKey);
            var outcome = exception is StorageUnavailableException ? CatalogOutcome.StorageUnavailable : CatalogOutcome.Busy;
            return Rejected(new LogoUpload(outcome, null), comparsaId);
        }
        catch (Exception exception)
        {
            // Before the commit was attempted the reference surely does not exist; during it, the
            // commit may have succeeded, so the image is left to the sweep (never a reference without an image).
            if (save.CommitAttempted)
            {
                LogLeftForSweep(logger, comparsaId, logo.Id, exception);
            }
            else
            {
                await objects.DeleteAsync(logo.ObjectKey);
            }

            throw;
        }

        // After the commit (and outside the transaction) the replaced image is unreferenced.
        if (save.Outcome != CatalogOutcome.Done)
        {
            await objects.DeleteAsync(logo.ObjectKey);
            return Rejected(new LogoUpload(save.Outcome, null), comparsaId);
        }

        await objects.DeleteAsync(save.ReplacedKey);
        return new LogoUpload(CatalogOutcome.Done, logo);
    }

    /// <summary>The logo's stored image, opened for reading, for a comparsa in the caller's scope; the caller disposes it.</summary>
    public async Task<(CatalogOutcome Outcome, StoredObject? Image)> OpenAsync(Guid comparsaId, CancellationToken cancellationToken)
    {
        var access = await scope.GetAccessAsync(cancellationToken);
        try
        {
            var (read, _, image) = await reader.OpenAsync(access.Filter(db.Comparsas, c => c.Id).Where(c => c.Id == comparsaId), cancellationToken);
            return read switch
            {
                LogoRead.Done => (CatalogOutcome.Done, image),
                LogoRead.ComparsaNotFound => (CatalogOutcome.ComparsaNotFound, null),
                _ => (CatalogOutcome.LogoNotFound, null),
            };
        }
        catch (StorageUnavailableException)
        {
            return (CatalogOutcome.StorageUnavailable, null);
        }
    }

    /// <summary>Clears the comparsa's logo and then erases its image; the storage is not needed to commit.</summary>
    public async Task<CatalogOutcome> RemoveAsync(Guid comparsaId, CancellationToken cancellationToken)
    {
        string removedKey;
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var comparsa = await db.LockComparsaForChangeAsync(comparsaId, cancellationToken);
            if (comparsa is null)
            {
                return CatalogOutcome.ComparsaNotFound;
            }

            if (comparsa.Logo is not { } logo)
            {
                return CatalogOutcome.LogoNotFound;
            }

            removedKey = logo.ObjectKey;
            comparsa.Logo = null;
            Record("ComparsaLogoRemoved", comparsaId);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception exception) when (CatalogLocks.IsRetryable(exception))
        {
            db.ChangeTracker.Clear();
            return CatalogOutcome.Busy;
        }

        await objects.DeleteAsync(removedKey);
        return CatalogOutcome.Done;
    }

    private ComparsaLogo NewLogo(NormalizedImage image)
    {
        var now = time.GetUtcNow();
        var id = Guid.CreateVersion7(now);
        return new ComparsaLogo
        {
            Id = id,
            ObjectKey = LogoStorage.KeyFor(id),
            Width = image.Width,
            Height = image.Height,
            SizeBytes = image.Content.Length,
            UploadedAt = now,
        };
    }

    /// <summary>
    /// Stores the reference in one transaction under the comparsa's row lock (a concurrent
    /// replacement waits and then replaces this one), audits it and commits. The outcome and the
    /// replaced key are left in <paramref name="save"/>, so the caller erases images outside the transaction.
    /// </summary>
    private async Task SaveAsync(Guid comparsaId, ComparsaLogo logo, LogoSave save, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var comparsa = await db.LockComparsaForChangeAsync(comparsaId, cancellationToken);
        if (comparsa is null)
        {
            // Deleted since the existence check.
            save.Outcome = CatalogOutcome.ComparsaNotFound;
            return;
        }

        save.ReplacedKey = comparsa.Logo?.ObjectKey;
        comparsa.Logo = logo;
        Record("ComparsaLogoUploaded", comparsaId, new { replaced = save.ReplacedKey is not null });
        await db.SaveChangesAsync(cancellationToken);
        save.CommitAttempted = true;
        await transaction.CommitAsync(cancellationToken);
        save.Outcome = CatalogOutcome.Done;
    }

    /// <summary>Audit entries never hold image data, keys, sizes or dimensions (design D7).</summary>
    private void Record(string action, Guid comparsaId, object? data = null) =>
        trail.Record(db, new AuditRecord(action, ComparsaAdministration.EntityType, comparsaId.ToString(), data, ComparsaId: comparsaId));

    /// <summary>Rejections are logged with ids, reasons and the acting user only, so saturation and probing are visible.</summary>
    private LogoUpload Rejected(LogoUpload upload, Guid comparsaId)
    {
        var reason = upload.Rejection?.ToString() ?? upload.Outcome.ToString();
        LogRejected(logger, reason, comparsaId, currentUser.UserId);
        return upload;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Logo upload rejected with {Reason} for comparsa {ComparsaId} by user {UserId}")]
    private static partial void LogRejected(ILogger logger, string reason, Guid comparsaId, Guid? userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Upload of logo {LogoId} for comparsa {ComparsaId} failed at the commit; its image is left to the orphan sweep")]
    private static partial void LogLeftForSweep(ILogger logger, Guid comparsaId, Guid logoId, Exception exception);

    /// <summary>The state of one upload's save, read after its transaction has ended.</summary>
    private sealed class LogoSave
    {
        public CatalogOutcome Outcome { get; set; } = CatalogOutcome.ComparsaNotFound;

        /// <summary>Set just before the commit: from then on the reference may exist.</summary>
        public bool CommitAttempted { get; set; }

        public string? ReplacedKey { get; set; }
    }
}
