using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PolvorApp.FederationCatalog.Comparsas;
using PolvorApp.SharedKernel.Storage;

namespace PolvorApp.FederationCatalog.Logos;

/// <summary>Why <see cref="LogoReader.OpenAsync"/> returned no image.</summary>
internal enum LogoRead
{
    Done,
    ComparsaNotFound,
    LogoNotFound,
}

/// <summary>
/// Opens a comparsa's stored logo for the API and for other modules (design D4, D6). A replacement
/// may commit and erase the image between reading the reference and reading the image, so a miss
/// re-reads the reference once; only a reference that still has no image is an inconsistency worth
/// an Error. Storage failures surface as <see cref="StorageUnavailableException"/>.
/// </summary>
internal sealed partial class LogoReader(IObjectStorage storage, ILogger<LogoReader> logger)
{
    /// <param name="comparsa">The comparsa to read, already filtered to the caller's scope when there is one.</param>
    public async Task<(LogoRead Outcome, ComparsaLogo? Logo, StoredObject? Image)> OpenAsync(IQueryable<Comparsa> comparsa, CancellationToken cancellationToken)
    {
        Guid? missing = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var found = await comparsa.AsNoTracking().Select(c => new { c.Logo }).SingleOrDefaultAsync(cancellationToken);
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

    [LoggerMessage(Level = LogLevel.Error, Message = "Logo {LogoId} has no stored image")]
    private static partial void LogMissingImage(ILogger logger, Guid logoId);
}
