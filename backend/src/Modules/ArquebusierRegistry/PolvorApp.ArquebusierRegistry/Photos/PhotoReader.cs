using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ArquebusierRegistry.Persistence;
using PolvorApp.SharedKernel.Storage;

namespace PolvorApp.ArquebusierRegistry.Photos;

/// <summary>
/// Opens a photo's stored image, without any scope check: callers check it first (the photo route) or
/// are Admin-only (the badges). A replacement may commit and erase the image between reading the
/// reference and reading the image, so the reference is read once more (design D5 of
/// add-arquebusier-photos; design D3 of add-badges). Storage outages are thrown to the caller.
/// </summary>
internal sealed partial class PhotoReader(ArquebusierRegistryDbContext db, IObjectStorage storage, ILogger<PhotoReader> logger)
{
    /// <summary>The stored image, which the caller disposes; null without a photo of that kind or when its image is gone.</summary>
    public async Task<StoredObject?> OpenAsync(Guid arquebusierId, ArquebusierPhotoKind kind, CancellationToken cancellationToken)
    {
        Guid? missing = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var photo = await db.Photos.AsNoTracking().SingleOrDefaultAsync(p => p.ArquebusierId == arquebusierId && p.Kind == kind, cancellationToken);
            if (photo is null)
            {
                return null;
            }

            if (photo.Id == missing)
            {
                break;
            }

            if (await storage.GetAsync(photo.ObjectKey, cancellationToken) is { } stored)
            {
                return stored;
            }

            missing = photo.Id;
        }

        // A reference without an image breaks the write order (design D2): worth an alert.
        LogMissingImage(logger, arquebusierId, missing!.Value);
        return null;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Photo {PhotoId} of arquebusier {ArquebusierId} has no stored image")]
    private static partial void LogMissingImage(ILogger logger, Guid arquebusierId, Guid photoId);
}
