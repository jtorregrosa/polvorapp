using PolvorApp.Api.Platform.Images;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.SharedKernel.Images;
using SkiaSharp;

namespace PolvorApp.Api.Tests.Images;

/// <summary>Spec: Photo validation and processing (SEC-12, NFR-15), design D3.</summary>
public sealed class SkiaImageNormalizerTests : IDisposable
{
    private const int TenMegabytes = 10 * 1024 * 1024;

    /// <summary>The ID photo rules of the registry: 3:4 within 1 %, 600 × 800 to 1200 × 1600.</summary>
    private static readonly ImageRules IdPhoto = new()
    {
        MaxInputBytes = TenMegabytes,
        MaxInputPixels = 40_000_000,
        FixedAspect = new ImageAspect(3, 4),
        AspectTolerance = 0.01,
        MinWidth = 600,
        MinHeight = 800,
        MaxWidth = 1200,
        MaxHeight = 1600,
    };

    /// <summary>The license photo rules: long side 800 to 2000, sides within a factor of 2.</summary>
    private static readonly ImageRules LicensePhoto = new()
    {
        MaxInputBytes = TenMegabytes,
        MaxInputPixels = 40_000_000,
        MaxSideRatio = 2,
        MinLongSide = 800,
        MaxWidth = 2000,
        MaxHeight = 2000,
    };

    private readonly SkiaImageNormalizer _normalizer = new(Microsoft.Extensions.Logging.Abstractions.NullLogger<SkiaImageNormalizer>.Instance);

    public void Dispose() => _normalizer.Dispose();

    [Fact]
    public async Task Exif_and_every_other_metadata_segment_is_removed()
    {
        var input = TestImages.JpegWithExif(600, 800);
        Assert.Contains((byte)0xE1, TestImages.JpegMarkers(input));

        var output = await NormalizeAsync(input, IdPhoto);

        var markers = TestImages.JpegMarkers(output.Content.Span);
        Assert.DoesNotContain((byte)0xE1, markers); // EXIF and XMP
        Assert.DoesNotContain((byte)0xE2, markers); // ICC profile
        Assert.DoesNotContain((byte)0xED, markers); // IPTC
        Assert.DoesNotContain((byte)0xFE, markers); // comments
        Assert.Equal(-1, output.Content.Span.IndexOf("SyntheticCam"u8));
        Assert.Equal("image/jpeg", output.ContentType);
    }

    [Fact]
    public async Task Xmp_icc_iptc_comments_and_trailing_bytes_are_removed()
    {
        var input = TestImages.JpegWithAllMetadata(600, 800);

        var output = await NormalizeAsync(input, IdPhoto);

        var content = output.Content.Span;
        Assert.DoesNotContain(TestImages.JpegMarkers(content), m => m is 0xE1 or 0xE2 or 0xED or 0xFE);
        foreach (var text in new[] { "SyntheticXmp", "ICC_PROFILE", "SyntheticIptc", "SyntheticComment", "TRAILING-SYNTHETIC-PAYLOAD", "SyntheticCam" })
        {
            Assert.Equal(-1, content.IndexOf(System.Text.Encoding.ASCII.GetBytes(text)));
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public async Task Every_exif_orientation_ends_upright(int orientation)
    {
        var input = TestImages.JpegWithExif(600, 800, orientation);

        var output = await NormalizeAsync(input, IdPhoto);

        Assert.Equal((600, 800), (output.Width, output.Height));
        AssertUprightQuadrants(output);
    }

    [Fact]
    public async Task Png_is_re_encoded_as_jpeg_on_a_white_background()
    {
        var output = await NormalizeAsync(TestImages.Png(1000, 700, transparent: true), LicensePhoto);

        Assert.Equal("image/jpeg", output.ContentType);
        Assert.Equal([0xFF, 0xD8], output.Content[..2].ToArray());
        Assert.Equal((1000, 700), (output.Width, output.Height));
    }

    [Fact]
    public async Task Transparency_is_flattened_onto_white_in_a_jpeg()
    {
        var output = await NormalizeAsync(TestImages.Png(1000, 700, transparent: true), LicensePhoto);

        // Half-transparent red over white is pink; over black it would be dark red.
        Assert.True(TestImages.Near(TestImages.PixelAt(output.Content.Span, 250, 175), new SkiaSharp.SKColor(255, 128, 128)));
    }

    [Fact]
    public async Task Just_over_one_percent_off_3_4_has_the_wrong_shape() =>
        await AssertRejectedAsync(TestImages.Jpeg(610, 800), IdPhoto, ImageRejection.AspectRatio);

    [Fact]
    public async Task Sides_exactly_twice_as_long_are_accepted()
    {
        var output = await NormalizeAsync(TestImages.Jpeg(1600, 800), LicensePhoto);

        Assert.Equal((1600, 800), (output.Width, output.Height));
    }

    [Fact]
    public async Task A_cancelled_request_stops()
    {
        using var stream = new MemoryStream(TestImages.Jpeg(600, 800));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _normalizer.NormalizeAsync(stream, IdPhoto, new CancellationToken(canceled: true)));
    }

    [Fact]
    public async Task Webp_is_re_encoded_as_jpeg()
    {
        var output = await NormalizeAsync(TestImages.Webp(600, 800), IdPhoto);

        Assert.Equal([0xFF, 0xD8], output.Content[..2].ToArray());
        AssertUprightQuadrants(output);
    }

    [Fact]
    public async Task A_pdf_is_an_unsupported_format() =>
        await AssertRejectedAsync(TestImages.PdfHeader(), IdPhoto, ImageRejection.UnsupportedFormat);

    [Fact]
    public async Task Random_bytes_are_an_unsupported_format() =>
        await AssertRejectedAsync([.. Enumerable.Range(0, 4096).Select(i => (byte)(i * 31 % 251))], IdPhoto, ImageRejection.UnsupportedFormat);

    [Fact]
    public async Task An_empty_file_is_an_unsupported_format() =>
        await AssertRejectedAsync([], IdPhoto, ImageRejection.UnsupportedFormat);

    [Fact]
    public async Task A_truncated_jpeg_is_an_unsupported_format() =>
        await AssertRejectedAsync(TestImages.Jpeg(600, 800)[..200], IdPhoto, ImageRejection.UnsupportedFormat);

    [Fact]
    public async Task An_image_over_the_pixel_limit_is_refused_from_its_header() =>
        await AssertRejectedAsync(TestImages.PngHeaderOnly(7000, 6000), IdPhoto, ImageRejection.TooLarge);

    [Fact]
    public async Task A_file_over_the_byte_limit_is_too_large() =>
        await AssertRejectedAsync(TestImages.Jpeg(600, 800), IdPhoto with { MaxInputBytes = 1000 }, ImageRejection.TooLarge);

    [Fact]
    public async Task A_3_4_photo_within_one_percent_is_cropped_to_exactly_3_4()
    {
        var output = await NormalizeAsync(TestImages.Jpeg(604, 800), IdPhoto);

        Assert.Equal((600, 800), (output.Width, output.Height));
    }

    [Fact]
    public async Task A_square_id_photo_has_the_wrong_shape() =>
        await AssertRejectedAsync(TestImages.Jpeg(1000, 1000), IdPhoto, ImageRejection.AspectRatio);

    [Fact]
    public async Task A_landscape_photo_is_not_a_portrait_id_photo() =>
        await AssertRejectedAsync(TestImages.Jpeg(1600, 1200), IdPhoto, ImageRejection.AspectRatio);

    [Fact]
    public async Task A_small_id_photo_is_too_small() =>
        await AssertRejectedAsync(TestImages.Jpeg(300, 400), IdPhoto, ImageRejection.TooSmall);

    [Fact]
    public async Task A_large_id_photo_is_scaled_down_to_1200_by_1600()
    {
        var output = await NormalizeAsync(TestImages.Jpeg(3000, 4000), IdPhoto);

        Assert.Equal((1200, 1600), (output.Width, output.Height));
        AssertUprightQuadrants(output);
    }

    [Fact]
    public async Task Images_are_never_scaled_up()
    {
        var output = await NormalizeAsync(TestImages.Jpeg(900, 1200), IdPhoto);

        Assert.Equal((900, 1200), (output.Width, output.Height));
    }

    [Fact]
    public async Task A_license_photo_with_a_short_long_side_is_too_small() =>
        await AssertRejectedAsync(TestImages.Jpeg(799, 500), LicensePhoto, ImageRejection.TooSmall);

    [Fact]
    public async Task A_too_elongated_license_photo_has_the_wrong_shape() =>
        await AssertRejectedAsync(TestImages.Jpeg(2100, 1000), LicensePhoto, ImageRejection.AspectRatio);

    [Fact]
    public async Task A_large_license_photo_is_scaled_to_a_2000_pixel_long_side()
    {
        var output = await NormalizeAsync(TestImages.Jpeg(4000, 2500), LicensePhoto);

        Assert.Equal((2000, 1250), (output.Width, output.Height));
    }

    [Fact]
    public async Task A_portrait_license_photo_keeps_its_orientation()
    {
        var output = await NormalizeAsync(TestImages.JpegWithExif(1000, 1500, orientation: 6), LicensePhoto);

        Assert.Equal((1000, 1500), (output.Width, output.Height));
        AssertUprightQuadrants(output);
    }

    [Fact]
    public async Task Png_output_keeps_transparency()
    {
        var output = await NormalizeAsync(TestImages.Png(1000, 700, transparent: true), LicensePhoto with { Output = ImageOutputFormat.Png });

        Assert.Equal("image/png", output.ContentType);
        Assert.True(TestImages.PixelAt(output.Content.Span, 10, 10).Alpha < 255);
    }

    private static void AssertUprightQuadrants(NormalizedImage output)
    {
        var (w, h) = (output.Width, output.Height);
        var content = output.Content.Span;
        Assert.True(TestImages.Near(TestImages.PixelAt(content, w / 4, h / 4), TestImages.TopLeft), "top left");
        Assert.True(TestImages.Near(TestImages.PixelAt(content, 3 * w / 4, h / 4), TestImages.TopRight), "top right");
        Assert.True(TestImages.Near(TestImages.PixelAt(content, w / 4, 3 * h / 4), TestImages.BottomLeft), "bottom left");
        Assert.True(TestImages.Near(TestImages.PixelAt(content, 3 * w / 4, 3 * h / 4), TestImages.BottomRight), "bottom right");
    }

    private async Task<NormalizedImage> NormalizeAsync(byte[] input, ImageRules rules)
    {
        using var stream = new MemoryStream(input);
        var result = await _normalizer.NormalizeAsync(stream, rules, TestContext.Current.CancellationToken);
        return Assert.IsType<NormalizedImage>(result);
    }

    private async Task AssertRejectedAsync(byte[] input, ImageRules rules, ImageRejection reason)
    {
        using var stream = new MemoryStream(input);
        var result = await _normalizer.NormalizeAsync(stream, rules, TestContext.Current.CancellationToken);
        Assert.Equal(new RejectedImage(reason), result);
    }
}
