using PolvorApp.SharedKernel.Images;
using SkiaSharp;

namespace PolvorApp.Api.Platform.Images;

/// <summary>
/// <see cref="IImageNormalizer"/> on SkiaSharp (ADR-0005, design D3). The pipeline is:
/// <list type="number">
/// <item>wait for the single processing slot (bounded), then read at most the byte limit;</item>
/// <item>detect the format from the content and read the dimensions from the header;</item>
/// <item>check the pixel count and every rule on the upright header dimensions, before any pixel is decoded;</item>
/// <item>decode to sRGB, scaled down by the codec when the source is much larger than the output (JPEG);</item>
/// <item>turn upright from the EXIF orientation, crop to the fixed shape, scale down, and encode a fresh image.</item>
/// </list>
/// A fresh encode carries no EXIF, XMP, IPTC, ICC or comment segment, and nothing hidden in the
/// upload survives it.
/// </summary>
internal sealed partial class SkiaImageNormalizer(ILogger<SkiaImageNormalizer> logger) : IImageNormalizer, IDisposable
{
    /// <summary>
    /// One image at a time: a 40 MP source decodes to about 160 MB, plus its upright copy. Uploads
    /// are cropped in the browser, so real ones are small and quick (design D3).
    /// </summary>
    private const int MaxConcurrentImages = 1;
    private const int JpegQuality = 85;
    private static readonly TimeSpan SlotWait = TimeSpan.FromSeconds(30);

    private static readonly SKSamplingOptions Sampling = new(SKCubicResampler.Mitchell);
    private static readonly SKSamplingOptions Exact = new(SKFilterMode.Nearest);
    private static readonly HashSet<SKEncodedImageFormat> AcceptedFormats = [SKEncodedImageFormat.Jpeg, SKEncodedImageFormat.Png, SKEncodedImageFormat.Webp];

    private readonly SemaphoreSlim _slots = new(MaxConcurrentImages);

    public async Task<ImageNormalization> NormalizeAsync(Stream input, ImageRules rules, CancellationToken cancellationToken)
    {
        // The slot is taken before buffering, so queued uploads do not each hold their bytes in memory.
        if (!await _slots.WaitAsync(SlotWait, cancellationToken))
        {
            throw new ImageProcessingBusyException();
        }

        try
        {
            var bytes = await ReadLimitedAsync(input, rules.MaxInputBytes, cancellationToken);
            return bytes is null ? new RejectedImage(ImageRejection.TooLarge) : Normalize(bytes, rules, cancellationToken);
        }
        finally
        {
            _slots.Release();
        }
    }

    public void Dispose() => _slots.Dispose();

    private ImageNormalization Normalize(byte[] bytes, ImageRules rules, CancellationToken cancellationToken)
    {
        using var data = SKData.CreateCopy(bytes);
        using var codec = data is null ? null : SKCodec.Create(data);
        if (codec is null || !AcceptedFormats.Contains(codec.EncodedFormat))
        {
            LogUnsupported(logger, codec is null ? null : codec.EncodedFormat);
            return new RejectedImage(ImageRejection.UnsupportedFormat);
        }

        // From the header, before any pixel is decoded (decompression bombs, wasted work).
        var stored = codec.Info;
        if ((long)stored.Width * stored.Height > rules.MaxInputPixels)
        {
            return new RejectedImage(ImageRejection.TooLarge);
        }

        var swapped = SwapsSides(codec.EncodedOrigin);
        var (width, height) = swapped ? (stored.Height, stored.Width) : (stored.Width, stored.Height);
        if (Check(width, height, rules) is { } rejection)
        {
            return new RejectedImage(rejection);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var decodeScale = OutputScale(width, height, rules);
        using var decoded = Decode(codec, decodeScale);
        if (decoded is null)
        {
            return new RejectedImage(ImageRejection.UnsupportedFormat);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var upright = Orient(decoded, codec.EncodedOrigin);
        try
        {
            var crop = CropFor(upright.Width, upright.Height, rules);
            var (outWidth, outHeight) = Fit(crop.Width, crop.Height, rules);
            using var output = Render(upright, crop, outWidth, outHeight, rules.Output);
            return Encode(output, rules.Output);
        }
        finally
        {
            if (!ReferenceEquals(upright, decoded))
            {
                upright.Dispose();
            }
        }
    }

    /// <summary>Null when the stream holds more than <paramref name="limit"/> bytes.</summary>
    private static async Task<byte[]?> ReadLimitedAsync(Stream input, long limit, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await input.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > limit)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// Decodes to 8-bit sRGB, at <paramref name="scale"/> when the codec supports it (JPEG decodes at
    /// eighths, never below the requested scale). Null when the data is truncated or corrupt.
    /// </summary>
    private SKBitmap? Decode(SKCodec codec, float scale)
    {
        var size = DecodeSize(codec, scale);
        var info = new SKImageInfo(size.Width, size.Height, SKColorType.Rgba8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb());
        var bitmap = new SKBitmap(info);
        if (bitmap.GetPixels() == IntPtr.Zero)
        {
            bitmap.Dispose();
            throw new InvalidOperationException("Could not allocate the image buffer.");
        }

        var result = codec.GetPixels(info, bitmap.GetPixels());
        if (result is SKCodecResult.Success)
        {
            return bitmap;
        }

        bitmap.Dispose();
        LogDecodeFailed(logger, result);
        return result is SKCodecResult.InternalError
            ? throw new InvalidOperationException($"The image decoder failed ({result}).")
            : null;
    }

    /// <summary>
    /// The smallest size the codec can decode to that is still at least <paramref name="scale"/> of the
    /// full size. Codecs round to the scales they support (JPEG: eighths), sometimes downwards.
    /// </summary>
    private static SKSizeI DecodeSize(SKCodec codec, float scale)
    {
        var full = codec.Info.Size;
        if (scale >= 1f)
        {
            return full;
        }

        var (neededWidth, neededHeight) = ((int)Math.Ceiling(full.Width * scale), (int)Math.Ceiling(full.Height * scale));
        for (var eighths = (int)Math.Ceiling(scale * 8); eighths < 8; eighths++)
        {
            var size = codec.GetScaledDimensions(eighths / 8f);
            if (size.Width >= neededWidth && size.Height >= neededHeight)
            {
                return size;
            }
        }

        return full;
    }

    private static bool SwapsSides(SKEncodedOrigin origin) =>
        origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;

    /// <summary>Applies the EXIF orientation: maps stored pixels (W × H) to the upright image; returns the input when it already is.</summary>
    private static SKBitmap Orient(SKBitmap stored, SKEncodedOrigin origin)
    {
        var (w, h) = ((float)stored.Width, (float)stored.Height);
        SKMatrix? matrix = origin switch
        {
            SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),
            SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightTop => new SKMatrix(0, -1, h, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, h, -1, 0, w, 0, 0, 1),
            SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, w, 0, 0, 1),
            _ => null,
        };
        if (matrix is not { } transform)
        {
            return stored;
        }

        var swap = SwapsSides(origin);
        var upright = new SKBitmap(stored.Info.WithSize(swap ? stored.Height : stored.Width, swap ? stored.Width : stored.Height));
        using var canvas = new SKCanvas(upright);
        canvas.Clear(SKColors.Transparent);
        canvas.SetMatrix(transform);

        // Immutable: the image shares the pixels instead of copying them.
        stored.SetImmutable();
        using var image = SKImage.FromBitmap(stored);
        canvas.DrawImage(image, 0, 0, Exact, null);
        return upright;
    }

    private static ImageRejection? Check(int width, int height, ImageRules rules)
    {
        if (rules.FixedAspect is { } aspect)
        {
            var ratio = (double)width / height / ((double)aspect.Width / aspect.Height);
            if (Math.Abs(ratio - 1) > rules.AspectTolerance + 1e-9)
            {
                return ImageRejection.AspectRatio;
            }
        }
        else if ((double)Math.Max(width, height) / Math.Min(width, height) > rules.MaxSideRatio)
        {
            return ImageRejection.AspectRatio;
        }

        var crop = CropFor(width, height, rules);
        var (outWidth, outHeight) = Fit(crop.Width, crop.Height, rules);
        return crop.Width < Math.Max(1, rules.MinWidth) || crop.Height < Math.Max(1, rules.MinHeight)
            || Math.Max(crop.Width, crop.Height) < rules.MinLongSide || outWidth < 1 || outHeight < 1
            ? ImageRejection.TooSmall
            : null;
    }

    /// <summary>The centred region with exactly the fixed shape (whole multiples of it), or the whole image.</summary>
    private static SKRectI CropFor(int width, int height, ImageRules rules)
    {
        if (rules.FixedAspect is not { } aspect)
        {
            return new SKRectI(0, 0, width, height);
        }

        var units = Math.Min(width / aspect.Width, height / aspect.Height);
        var (cropWidth, cropHeight) = (units * aspect.Width, units * aspect.Height);
        var (left, top) = ((width - cropWidth) / 2, (height - cropHeight) / 2);
        return new SKRectI(left, top, left + cropWidth, top + cropHeight);
    }

    /// <summary>Scales down to fit the maximum size, keeping the shape; never scales up.</summary>
    private static (int Width, int Height) Fit(int width, int height, ImageRules rules)
    {
        var scale = Math.Min((double)rules.MaxWidth / width, (double)rules.MaxHeight / height);
        if (scale >= 1d)
        {
            return (width, height);
        }

        if (rules.FixedAspect is { } aspect)
        {
            var units = Math.Min(rules.MaxWidth / aspect.Width, rules.MaxHeight / aspect.Height);
            return (units * aspect.Width, units * aspect.Height);
        }

        return ((int)Math.Round(width * scale), (int)Math.Round(height * scale));
    }

    /// <summary>How much the upright image will shrink at most; the codec may decode at this scale or larger.</summary>
    private static float OutputScale(int width, int height, ImageRules rules)
    {
        var crop = CropFor(width, height, rules);
        var (outWidth, outHeight) = Fit(crop.Width, crop.Height, rules);
        return (float)Math.Min(1d, Math.Max((double)outWidth / crop.Width, (double)outHeight / crop.Height));
    }

    private static SKBitmap Render(SKBitmap upright, SKRectI crop, int width, int height, ImageOutputFormat format)
    {
        var output = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb()));
        using var canvas = new SKCanvas(output);

        // JPEG has no transparency: transparent areas become white, not black.
        canvas.Clear(format == ImageOutputFormat.Jpeg ? SKColors.White : SKColors.Transparent);

        upright.SetImmutable();
        using var image = SKImage.FromBitmap(upright);
        canvas.DrawImage(image, crop, new SKRect(0, 0, width, height), Sampling, null);
        return output;
    }

    /// <summary>Encodes without a colour profile: the pixels are already sRGB, the web's default.</summary>
    private static NormalizedImage Encode(SKBitmap bitmap, ImageOutputFormat format)
    {
        using var pixmap = bitmap.PeekPixels();
        using var plain = new SKPixmap(pixmap.Info.WithColorSpace(null), pixmap.GetPixels(), pixmap.RowBytes);
        var (skiaFormat, contentType, quality) = format == ImageOutputFormat.Png
            ? (SKEncodedImageFormat.Png, "image/png", 100)
            : (SKEncodedImageFormat.Jpeg, "image/jpeg", JpegQuality);
        using var data = plain.Encode(skiaFormat, quality)
            ?? throw new InvalidOperationException("The image could not be encoded.");
        return new NormalizedImage(data.ToArray(), bitmap.Width, bitmap.Height, contentType);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Image refused: format {Format} is not accepted (none means unrecognised)")]
    private static partial void LogUnsupported(ILogger logger, SKEncodedImageFormat? format);

    [LoggerMessage(Level = LogLevel.Information, Message = "Image refused: decoding ended with {Result}")]
    private static partial void LogDecodeFailed(ILogger logger, SKCodecResult result);
}
