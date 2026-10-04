namespace PolvorApp.ArquebusierRegistry.Contracts;

/// <summary>
/// Read contract of the registry for the badges (add-badges, design D3): an arquebusier's ID photo, as
/// stored. Server-side only.
/// </summary>
/// <remarks>
/// It applies no comparsa scope: only Admin-only callers may use it, whose scope is the whole
/// Federation (BR-12). It throws <c>StorageUnavailableException</c> when the storage cannot be read,
/// so a caller never prints a document with a photo silently left out.
/// </remarks>
public interface IIdPhotoReader
{
    /// <summary>
    /// The ID photo, read once more if it was replaced meanwhile; null when the arquebusier is unknown,
    /// has no ID photo, or its image is gone or larger than <see cref="IdPhoto.MaxBytes"/> (logged as an
    /// alert).
    /// </summary>
    /// <exception cref="PolvorApp.SharedKernel.Storage.StorageUnavailableException">The storage cannot be read.</exception>
    Task<IdPhoto?> ReadAsync(Guid arquebusierId, CancellationToken cancellationToken);
}

/// <summary>An ID photo as stored: a 3:4 JPEG without metadata (NFR-15, SEC-12), at most 1200 × 1600 px.</summary>
public sealed class IdPhoto
{
    /// <summary>Far above any stored ID photo; a larger object is not one the registry wrote.</summary>
    public const int MaxBytes = 4 * 1024 * 1024;

    /// <param name="jpeg">The image, starting with the JPEG start-of-image marker.</param>
    public IdPhoto(ReadOnlyMemory<byte> jpeg)
    {
        if (jpeg.Length is < 2 or > MaxBytes || jpeg.Span[0] != 0xFF || jpeg.Span[1] != 0xD8)
        {
            throw new ArgumentException($"An ID photo is a JPEG of at most {MaxBytes} bytes.", nameof(jpeg));
        }

        Jpeg = jpeg;
    }

    public ReadOnlyMemory<byte> Jpeg { get; }

    /// <summary>The type name only: the image is personal data.</summary>
    public override string ToString() => nameof(IdPhoto);
}
