using PolvorApp.ArquebusierRegistry.Contracts;

namespace PolvorApp.ArquebusierRegistry.Photos;

/// <summary>
/// A stored photo of an arquebusier (spec: Arquebusier photos; design D4). It lives in its own table,
/// so uploading one never changes the arquebusier's version. The image itself is in the object
/// storage under <see cref="ObjectKey"/>, a random name that never derives from personal data.
/// </summary>
internal sealed class ArquebusierPhoto
{
    public const int ObjectKeyMaxLength = 200;

    /// <summary>A new one for every upload, so it also serves as the photo's version in URLs.</summary>
    public required Guid Id { get; init; }

    public required Guid ArquebusierId { get; init; }

    public required ArquebusierPhotoKind Kind { get; init; }

    public required string ObjectKey { get; init; }

    public required int Width { get; init; }

    public required int Height { get; init; }

    public required int SizeBytes { get; init; }

    public required DateTimeOffset UploadedAt { get; init; }

    /// <summary>The object key of a photo with id <paramref name="id"/> (modules README: object storage).</summary>
    public static string KeyFor(Guid id) => $"{PhotoStorage.Prefix}{id:N}.jpg";
}
