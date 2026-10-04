using Microsoft.Extensions.Logging;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.SharedKernel.Storage;

namespace PolvorApp.ArquebusierRegistry.Photos;

/// <summary>
/// <see cref="IIdPhotoReader"/> on the registry's photo reader (add-badges, design D3). The image is read
/// into memory up to <see cref="IdPhoto.MaxBytes"/>: a larger or malformed object is not one the
/// registry wrote, so it is reported and treated as missing, never read without bound.
/// </summary>
internal sealed partial class RegistryIdPhotoReader(PhotoReader reader, ILogger<RegistryIdPhotoReader> logger) : IIdPhotoReader
{
    public async Task<IdPhoto?> ReadAsync(Guid arquebusierId, CancellationToken cancellationToken)
    {
        if (await reader.OpenAsync(arquebusierId, ArquebusierPhotoKind.Id, cancellationToken) is not { } stored)
        {
            return null;
        }

        await using (stored)
        {
            if (stored.Length > IdPhoto.MaxBytes)
            {
                LogUnreadable(logger, arquebusierId, stored.Length);
                return null;
            }

            // One byte more than announced (or than the limit, when unknown) tells an object whose length was wrong.
            var buffer = new byte[(stored.Length >= 0 ? (int)stored.Length : IdPhoto.MaxBytes) + 1];
            var read = 0;
            int chunk;
            try
            {
                while (read < buffer.Length && (chunk = await stored.Content.ReadAsync(buffer.AsMemory(read), cancellationToken)) > 0)
                {
                    read += chunk;
                }
            }
            catch (Exception exception) when (exception is IOException or HttpRequestException or TimeoutException)
            {
                // The storage failed while sending the image: the caller fails as a whole (503), never without it.
                throw new StorageUnavailableException("A stored photo could not be read.", exception);
            }

            if (read == buffer.Length)
            {
                LogUnreadable(logger, arquebusierId, read);
                return null;
            }

            try
            {
                return new IdPhoto(buffer.AsMemory(0, read).ToArray());
            }
            catch (ArgumentException)
            {
                LogUnreadable(logger, arquebusierId, read);
                return null;
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The ID photo of arquebusier {ArquebusierId} is not a readable JPEG ({Length} bytes)")]
    private static partial void LogUnreadable(ILogger logger, Guid arquebusierId, long length);
}
