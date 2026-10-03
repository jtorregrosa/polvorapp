using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PolvorApp.FederationCatalog.Comparsas;
using PolvorApp.SharedKernel.Storage;

namespace PolvorApp.FederationCatalog.Logos;

/// <summary>
/// Why <see cref="LogoReader.OpenAsync(IQueryable{Comparsa}, CancellationToken)"/> returned no image.
/// <see cref="ComparsaNotFound"/> means the owner row is missing, whichever owner it is (the name
/// predates the Federation logo).
/// </summary>
internal enum LogoRead
{
    Done,
    ComparsaNotFound,
    LogoNotFound,
}

/// <summary>
/// Opens a stored logo — a comparsa's, or the Federation's — for the API and for other modules (design D4, D6). A replacement
/// may commit and erase the image between reading the reference and reading the image, so a miss
/// re-reads the reference once; only a reference that still has no image is an inconsistency worth
/// an Error. Storage failures surface as <see cref="StorageUnavailableException"/>.
/// </summary>
internal sealed partial class LogoReader(IObjectStorage storage, ILogger<LogoReader> logger)
{
    /// <param name="comparsa">The comparsa to read, already filtered to the caller's scope when there is one.</param>
    /// <param name="cancellationToken">The request's token.</param>
    public Task<(LogoRead Outcome, ComparsaLogo? Logo, StoredObject? Image)> OpenAsync(IQueryable<Comparsa> comparsa, CancellationToken cancellationToken) =>
        OpenAsync(async token => await comparsa.AsNoTracking().Select(c => new LogoReference(c.Logo)).SingleOrDefaultAsync(token), cancellationToken);

    /// <summary>The Federation's logo (add-distribution-planning, design D11); its settings row always exists.</summary>
    /// <param name="settings">The Federation's settings.</param>
    /// <param name="cancellationToken">The request's token.</param>
    public Task<(LogoRead Outcome, ComparsaLogo? Logo, StoredObject? Image)> OpenAsync(IQueryable<FederationSettings> settings, CancellationToken cancellationToken) =>
        OpenAsync(async token => await settings.AsNoTracking().Select(s => new LogoReference(s.Logo)).SingleOrDefaultAsync(token), cancellationToken);

    private async Task<(LogoRead Outcome, ComparsaLogo? Logo, StoredObject? Image)> OpenAsync(
        Func<CancellationToken, Task<LogoReference?>> readReference, CancellationToken cancellationToken)
    {
        Guid? missing = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var found = await readReference(cancellationToken);
            if (found is null)
            {
                return (LogoRead.ComparsaNotFound, null, null);
            }

            if (found.Logo is not { } logo)
            {
                return (LogoRead.LogoNotFound, null, null);
            }

            if (logo.Id == missing)
            {
                LogMissingImage(logger, logo.Id);
                return (LogoRead.LogoNotFound, null, null);
            }

            if (await storage.GetAsync(logo.ObjectKey, cancellationToken) is { } image)
            {
                return (LogoRead.Done, logo, image);
            }

            missing = logo.Id;
        }

        // Replaced twice while reading: the caller sees no logo this time, which is not an inconsistency.
        return (LogoRead.LogoNotFound, null, null);
    }

    /// <summary>Reads a whole opened image into memory; a failure mid-stream is a storage outage.</summary>
    public static async Task<byte[]> ReadAllAsync(StoredObject image, int expectedBytes, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream(capacity: expectedBytes);
        try
        {
            await image.Content.CopyToAsync(buffer, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or HttpRequestException or TimeoutException)
        {
            throw new StorageUnavailableException("The object storage failed while sending a logo.", exception);
        }

        return buffer.ToArray();
    }

    /// <summary>The logo column set of an owner row; null for an owner that does not exist.</summary>
    private sealed record LogoReference(ComparsaLogo? Logo);

    [LoggerMessage(Level = LogLevel.Error, Message = "Logo {LogoId} has no stored image")]
    private static partial void LogMissingImage(ILogger logger, Guid logoId);
}
