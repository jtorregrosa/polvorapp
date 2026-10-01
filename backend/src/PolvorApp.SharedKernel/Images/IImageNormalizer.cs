namespace PolvorApp.SharedKernel.Images;

/// <summary>
/// Turns an uploaded image into a clean, freshly encoded one (ADR-0005, SEC-12; design D3 of
/// add-arquebusier-photos): the format is detected from the content, the size is checked before
/// decoding, the image is turned upright, checked against <see cref="ImageRules"/>, cropped to a
/// fixed shape if the rules ask for one, scaled down to the maximum size and re-encoded without any
/// metadata. The uploaded bytes are never stored as they came.
/// </summary>
public interface IImageNormalizer
{
    /// <returns>A <see cref="NormalizedImage"/>, or a <see cref="RejectedImage"/> saying which rule failed.</returns>
    Task<ImageNormalization> NormalizeAsync(Stream input, ImageRules rules, CancellationToken cancellationToken);
}

/// <summary>What an image must satisfy and how it is stored. Dimensions apply after the image is turned upright.</summary>
public sealed record ImageRules
{
    /// <summary>Larger uploads are rejected with <see cref="ImageRejection.TooLarge"/>.</summary>
    public required long MaxInputBytes { get; init; }

    /// <summary>Width × height checked from the header, before decoding (decompression bombs).</summary>
    public required long MaxInputPixels { get; init; }

    /// <summary>A fixed shape (e.g. 3:4) the image must have within <see cref="AspectTolerance"/>; it is then cropped to it exactly.</summary>
    public ImageAspect? FixedAspect { get; init; }

    /// <summary>Relative tolerance for <see cref="FixedAspect"/>, e.g. 0.01 for 1 %.</summary>
    public double AspectTolerance { get; init; } = 0.01;

    /// <summary>Without a fixed shape: the long side may be at most this many times the short side.</summary>
    public double MaxSideRatio { get; init; } = double.PositiveInfinity;

    public int MinWidth { get; init; }

    public int MinHeight { get; init; }

    /// <summary>Minimum length of the longer side.</summary>
    public int MinLongSide { get; init; }

    /// <summary>Larger images are scaled down to fit; smaller ones are never scaled up.</summary>
    public required int MaxWidth { get; init; }

    public required int MaxHeight { get; init; }

    public ImageOutputFormat Output { get; init; } = ImageOutputFormat.Jpeg;
}

/// <summary>A width-to-height shape, e.g. 3:4 for a portrait ID photo.</summary>
public sealed record ImageAspect(int Width, int Height);

public enum ImageOutputFormat
{
    /// <summary>JPEG; transparent areas become white.</summary>
    Jpeg,

    /// <summary>PNG, keeping transparency (logos).</summary>
    Png,
}

/// <summary>Why an image was refused; maps to the API's field reasons.</summary>
public enum ImageRejection
{
    /// <summary>Too many bytes or too many pixels.</summary>
    TooLarge,

    /// <summary>Not a JPEG, PNG or WebP image, or it cannot be decoded.</summary>
    UnsupportedFormat,

    /// <summary>Below the minimum dimensions.</summary>
    TooSmall,

    /// <summary>Outside the fixed shape's tolerance, or too elongated.</summary>
    AspectRatio,
}

/// <summary>The outcome of <see cref="IImageNormalizer.NormalizeAsync"/>.</summary>
public abstract record ImageNormalization;

/// <summary>The re-encoded image, ready to store.</summary>
public sealed record NormalizedImage(ReadOnlyMemory<byte> Content, int Width, int Height, string ContentType) : ImageNormalization;

/// <summary>The image broke a rule; nothing should be stored.</summary>
public sealed record RejectedImage(ImageRejection Reason) : ImageNormalization;
