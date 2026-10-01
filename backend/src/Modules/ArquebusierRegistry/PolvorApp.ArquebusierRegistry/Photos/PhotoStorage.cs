using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.SharedKernel.Images;

namespace PolvorApp.ArquebusierRegistry.Photos;

/// <summary>Where and how the registry stores photos (spec: Photo validation and processing; design D3, D4).</summary>
internal static class PhotoStorage
{
    /// <summary>The registry's key prefix in the object storage.</summary>
    public const string Prefix = "registry/photos/";

    public const string ContentType = "image/jpeg";

    /// <summary>Largest upload accepted (spec: at most 10 MB).</summary>
    public const long MaxUploadBytes = 10 * 1024 * 1024;

    private const long MaxInputPixels = 40_000_000;

    /// <summary>NFR-15: 3:4 portrait within 1 %, at least 600 × 800, stored at most 1200 × 1600.</summary>
    public static readonly ImageRules IdPhoto = new()
    {
        MaxInputBytes = MaxUploadBytes,
        MaxInputPixels = MaxInputPixels,
        FixedAspect = new ImageAspect(3, 4),
        AspectTolerance = 0.01,
        MinWidth = 600,
        MinHeight = 800,
        MaxWidth = 1200,
        MaxHeight = 1600,
    };

    /// <summary>License sides: long side at least 800, sides within a factor of 2, stored at most 2000 on the long side.</summary>
    public static readonly ImageRules LicensePhoto = new()
    {
        MaxInputBytes = MaxUploadBytes,
        MaxInputPixels = MaxInputPixels,
        MaxSideRatio = 2,
        MinLongSide = 800,
        MaxWidth = 2000,
        MaxHeight = 2000,
    };

    public static ImageRules RulesFor(ArquebusierPhotoKind kind) => kind == ArquebusierPhotoKind.Id ? IdPhoto : LicensePhoto;

    public static bool NeedsLicense(ArquebusierPhotoKind kind) => kind != ArquebusierPhotoKind.Id;

    /// <summary>URL slugs (<c>id</c>, <c>license-front</c>, <c>license-back</c>): no upper-case codes in paths (design D4).</summary>
    public static bool TryParseSlug(string? slug, out ArquebusierPhotoKind kind)
    {
        (var known, kind) = slug switch
        {
            "id" => (true, ArquebusierPhotoKind.Id),
            "license-front" => (true, ArquebusierPhotoKind.LicenseFront),
            "license-back" => (true, ArquebusierPhotoKind.LicenseBack),
            _ => (false, default),
        };
        return known;
    }
}
