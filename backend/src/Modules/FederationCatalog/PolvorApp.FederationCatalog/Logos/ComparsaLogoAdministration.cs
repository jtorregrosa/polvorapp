using Microsoft.EntityFrameworkCore;
using PolvorApp.FederationCatalog.Comparsas;
using PolvorApp.FederationCatalog.Persistence;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.SharedKernel.Auditing;
using PolvorApp.SharedKernel.Storage;

namespace PolvorApp.FederationCatalog.Logos;

/// <summary>
/// Uploads, reads and removes comparsa logos (spec: Comparsa logos, Logo access; design D4). Writes
/// are Admin-only at the endpoint; reads are scoped to the caller's comparsas (BR-12). An upload goes
/// through <see cref="LogoUploadFlow"/>, which commits the reference with its audit entry under the
/// comparsa's row lock and only then erases the replaced image.
/// </summary>
internal sealed class ComparsaLogoAdministration(
    FederationCatalogDbContext db,
    IComparsaScope scope,
    IAuditTrail trail,
    LogoUploadFlow flow,
    LogoObjects objects,
    LogoReader reader)
{
    /// <summary>
    /// Whether the comparsa exists. Uploads ask this before the body is read, so unknown ids cost
    /// neither the upload nor any image work.
    /// </summary>
    public Task<bool> ExistsAsync(Guid comparsaId, CancellationToken cancellationToken) =>
        db.Comparsas.AsNoTracking().AnyAsync(c => c.Id == comparsaId, cancellationToken);

    /// <summary>Normalises and stores <paramref name="file"/> as the comparsa's logo, replacing any previous one.</summary>
    public Task<LogoUpload> UploadAsync(Guid comparsaId, Stream file, CancellationToken cancellationToken) =>
        flow.UploadAsync($"comparsa {comparsaId}", file, async (logo, token) =>
        {
            var comparsa = await db.LockComparsaForChangeAsync(comparsaId, token);
            if (comparsa is null)
            {
                return LogoSwap.Missing;
            }

            var replaced = comparsa.Logo?.ObjectKey;
            comparsa.Logo = logo;
            Record(FederationCatalogAuditActions.ComparsaLogoUploaded, comparsaId, new { replaced = replaced is not null });
            return new LogoSwap(true, replaced);
        }, cancellationToken);

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
            Record(FederationCatalogAuditActions.ComparsaLogoRemoved, comparsaId);
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

    /// <summary>Audit entries never hold image data, keys, sizes or dimensions (design D7).</summary>
    private void Record(string action, Guid comparsaId, object? data = null) =>
        trail.Record(db, new AuditRecord(action, ComparsaAdministration.EntityType, comparsaId.ToString(), data, ComparsaId: comparsaId));
}
