using Microsoft.Extensions.Logging;
using PolvorApp.FederationCatalog.Persistence;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.SharedKernel.Images;
using PolvorApp.SharedKernel.Storage;

namespace PolvorApp.FederationCatalog.Logos;

/// <summary>How a logo upload ended: the stored logo, or the outcome and, for an image that broke a rule, which rule.</summary>
internal sealed record LogoUpload(CatalogOutcome Outcome, ComparsaLogo? Logo, ImageRejection? Rejection = null);

/// <summary>
/// What swapping the reference did, inside the upload's transaction: whether the owner (a comparsa, or
/// the Federation) was found and locked, and the key of the image it replaced.
/// </summary>
internal sealed record LogoSwap(bool Found, string? ReplacedKey)
{
    /// <summary>The owner no longer exists.</summary>
    public static readonly LogoSwap Missing = new(false, null);
}

/// <summary>
/// The upload of a logo, shared by comparsa logos and the Federation logo (add-comparsa-logos design
/// D4; add-distribution-planning design D11). It normalises the image, stores it under a new random key,
/// lets the owner swap the reference (lock, set, audit) and commits, and only then erases the replaced
/// image. Every failure leaves at most an unreferenced image: erased at once when the reference was
/// surely not committed, and by the orphan sweep otherwise.
/// </summary>
internal sealed partial class LogoUploadFlow(
    FederationCatalogDbContext db,
    ICurrentUser currentUser,
    IObjectStorage storage,
    IImageNormalizer images,
    LogoObjects objects,
    TimeProvider time,
    ILogger<LogoUploadFlow> logger)
{
    /// <param name="owner">What the logo belongs to, for logs only, e.g. "comparsa {id}"; never personal data.</param>
    /// <param name="file">The uploaded image.</param>
    /// <param name="swap">
    /// Runs inside the transaction: locks the owner, sets the new logo, records the audit entry, and
    /// says what it replaced. The flow saves and commits.
    /// </param>
    /// <param name="cancellationToken">The request's token.</param>
    public async Task<LogoUpload> UploadAsync(
        string owner, Stream file, Func<ComparsaLogo, CancellationToken, Task<LogoSwap>> swap, CancellationToken cancellationToken)
    {
        ImageNormalization normalized;
        try
        {
            normalized = await images.NormalizeAsync(file, LogoStorage.Rules, cancellationToken);
        }
        catch (ImageProcessingBusyException)
        {
            return Rejected(new LogoUpload(CatalogOutcome.Busy, null), owner);
        }

        if (normalized is RejectedImage rejected)
        {
            return Rejected(new LogoUpload(CatalogOutcome.ImageRejected, null, rejected.Reason), owner);
        }

        var image = (NormalizedImage)normalized;
        var logo = NewLogo(image);
        var save = new LogoSave();
        try
        {
            await storage.PutAsync(logo.ObjectKey, image.Content, LogoStorage.ContentType, cancellationToken);
            await SaveAsync(logo, swap, save, cancellationToken);
        }
        catch (Exception exception) when (!save.CommitAttempted && (exception is StorageUnavailableException || CatalogLocks.IsRetryable(exception)))
        {
            // Nothing was committed: the new image (if the put landed) is unreferenced.
            db.ChangeTracker.Clear();
            await objects.DeleteAsync(logo.ObjectKey);
            var outcome = exception is StorageUnavailableException ? CatalogOutcome.StorageUnavailable : CatalogOutcome.Busy;
            return Rejected(new LogoUpload(outcome, null), owner);
        }
        catch (Exception exception)
        {
            // Before the commit was attempted the reference surely does not exist; during it, the
            // commit may have succeeded, so the image is left to the sweep (never a reference without an image).
            if (save.CommitAttempted)
            {
                LogLeftForSweep(logger, owner, logo.Id, exception);
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
            return Rejected(new LogoUpload(save.Outcome, null), owner);
        }

        await objects.DeleteAsync(save.ReplacedKey);
        return new LogoUpload(CatalogOutcome.Done, logo);
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
    /// Stores the reference in one transaction under the owner's row lock (a concurrent replacement
    /// waits and then replaces this one) and commits. The outcome and the replaced key are left in
    /// <paramref name="save"/>, so the caller erases images outside the transaction.
    /// </summary>
    private async Task SaveAsync(ComparsaLogo logo, Func<ComparsaLogo, CancellationToken, Task<LogoSwap>> swap, LogoSave save, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var swapped = await swap(logo, cancellationToken);
        if (!swapped.Found)
        {
            // Deleted since the existence check.
            save.Outcome = CatalogOutcome.ComparsaNotFound;
            return;
        }

        save.ReplacedKey = swapped.ReplacedKey;
        await db.SaveChangesAsync(cancellationToken);
        save.CommitAttempted = true;
        await transaction.CommitAsync(cancellationToken);
        save.Outcome = CatalogOutcome.Done;
    }

    /// <summary>Rejections are logged with the owner, the reason and the acting user only, so saturation and probing are visible.</summary>
    private LogoUpload Rejected(LogoUpload upload, string owner)
    {
        var reason = upload.Rejection?.ToString() ?? upload.Outcome.ToString();
        LogRejected(logger, reason, owner, currentUser.UserId);
        return upload;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Logo upload rejected with {Reason} for {Owner} by user {UserId}")]
    private static partial void LogRejected(ILogger logger, string reason, string owner, Guid? userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Upload of logo {LogoId} for {Owner} failed at the commit; its image is left to the orphan sweep")]
    private static partial void LogLeftForSweep(ILogger logger, string owner, Guid logoId, Exception exception);

    /// <summary>The state of one upload's save, read after its transaction has ended.</summary>
    private sealed class LogoSave
    {
        /// <summary>Until the swap finds its owner; <see cref="CatalogOutcome.ComparsaNotFound"/> stands for any missing owner.</summary>
        public CatalogOutcome Outcome { get; set; } = CatalogOutcome.ComparsaNotFound;

        /// <summary>Set just before the commit: from then on the reference may exist.</summary>
        public bool CommitAttempted { get; set; }

        public string? ReplacedKey { get; set; }
    }
}
