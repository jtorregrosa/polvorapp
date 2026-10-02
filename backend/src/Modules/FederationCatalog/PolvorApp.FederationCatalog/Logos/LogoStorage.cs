using PolvorApp.SharedKernel.Images;

namespace PolvorApp.FederationCatalog.Logos;

/// <summary>Where and how the catalogue stores comparsa logos (spec: Logo validation and processing; design D3).</summary>
internal static class LogoStorage
{
    /// <summary>The catalogue's key prefix in the object storage.</summary>
    public const string Prefix = "catalog/logos/";

    public const string ContentType = "image/png";

    /// <summary>Largest upload accepted (spec: at most 10 MB).</summary>
    public const long MaxUploadBytes = 10 * 1024 * 1024;

    /// <summary>Spec: an image over 40 megapixels is too large, checked before decoding.</summary>
    private const long MaxInputPixels = 40_000_000;

    /// <summary>
    /// Any shape whose long side is at least 256 px and at most 3 times the short side, stored as a
    /// PNG with its transparency, at most 1024 px on the long side.
    /// </summary>
    public static readonly ImageRules Rules = new()
    {
        MaxInputBytes = MaxUploadBytes,
        MaxInputPixels = MaxInputPixels,
        MinLongSide = 256,
        MaxSideRatio = 3,
        MaxWidth = 1024,
        MaxHeight = 1024,
        Output = ImageOutputFormat.Png,
    };

    /// <summary>The object key of the logo with id <paramref name="logoId"/>; random, never derived from the comparsa.</summary>
    public static string KeyFor(Guid logoId) => $"{Prefix}{logoId:N}.png";
}
