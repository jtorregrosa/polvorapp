namespace PolvorApp.FederationCatalog.Logos;

/// <summary>
/// The stored logo of a comparsa (spec: Comparsa logos; design D1): a reference to one PNG in the
/// private storage. It is replaced as a whole, never edited.
/// </summary>
internal sealed class ComparsaLogo
{
    /// <summary>Random id: the object name and the version the UI puts in the image URL.</summary>
    public required Guid Id { get; init; }

    public required string ObjectKey { get; init; }

    public required int Width { get; init; }

    public required int Height { get; init; }

    public required int SizeBytes { get; init; }

    public required DateTimeOffset UploadedAt { get; init; }
}
