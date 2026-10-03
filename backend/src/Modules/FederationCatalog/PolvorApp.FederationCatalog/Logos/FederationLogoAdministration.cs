using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PolvorApp.FederationCatalog.Persistence;
using PolvorApp.SharedKernel.Auditing;
using PolvorApp.SharedKernel.Storage;

namespace PolvorApp.FederationCatalog.Logos;

/// <summary>
/// Uploads, reads and removes the Federation's logo (spec: Federation logo; add-distribution-planning,
/// design D11). It is uploaded at run time and never committed to the repository. Writes are Admin-only
/// at the endpoint; every signed-in user reads it. Uploads go through <see cref="LogoUploadFlow"/>, under
/// the settings row's lock, exactly as comparsa logos do.
/// </summary>
internal sealed class FederationLogoAdministration(
    FederationCatalogDbContext db,
    IAuditTrail trail,
    LogoUploadFlow flow,
    LogoObjects objects,
    LogoReader reader,
    TimeProvider time)
{
    public const string EntityType = "FederationSettings";

    /// <summary>The logo as the API describes it, or null when none was uploaded.</summary>
    public Task<ComparsaLogo?> FindAsync(CancellationToken cancellationToken) =>
        db.FederationSettings.AsNoTracking().Select(s => s.Logo).SingleAsync(cancellationToken);

    /// <summary>Normalises and stores <paramref name="file"/> as the Federation's logo, replacing any previous one.</summary>
    public Task<LogoUpload> UploadAsync(Stream file, CancellationToken cancellationToken) =>
        flow.UploadAsync("the Federation", file, async (logo, token) =>
        {
            var settings = await db.LockFederationSettingsAsync(token);
            var replaced = settings.Logo?.ObjectKey;
            settings.Logo = logo;
            settings.UpdatedAt = logo.UploadedAt;
            Record("FederationLogoUploaded", new { replaced = replaced is not null });
            return new LogoSwap(true, replaced);
        }, cancellationToken);

    /// <summary>The logo's stored image, opened for reading; the caller disposes it.</summary>
    public async Task<(CatalogOutcome Outcome, StoredObject? Image)> OpenAsync(CancellationToken cancellationToken)
    {
        try
        {
            var (read, _, image) = await reader.OpenAsync(db.FederationSettings, cancellationToken);
            return read == LogoRead.Done ? (CatalogOutcome.Done, image) : (CatalogOutcome.LogoNotFound, null);
        }
        catch (StorageUnavailableException)
        {
            return (CatalogOutcome.StorageUnavailable, null);
        }
    }

    /// <summary>Clears the logo and then erases its image; the storage is not needed to commit.</summary>
    public async Task<CatalogOutcome> RemoveAsync(CancellationToken cancellationToken)
    {
        string removedKey;
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var settings = await db.LockFederationSettingsAsync(cancellationToken);
            if (settings.Logo is not { } logo)
            {
                return CatalogOutcome.LogoNotFound;
            }

            removedKey = logo.ObjectKey;
            settings.Logo = null;
            settings.UpdatedAt = time.GetUtcNow();
            Record("FederationLogoRemoved");
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

    /// <summary>Audit entries never hold image data, keys, sizes or dimensions; the Federation belongs to no comparsa.</summary>
    private void Record(string action, object? data = null) =>
        trail.Record(db, new AuditRecord(action, EntityType, FederationSettings.SingletonId.ToString(CultureInfo.InvariantCulture), data));
}
