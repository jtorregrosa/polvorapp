using System.Text;
using PolvorApp.Api.Platform.Images;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.FederationCatalog.Logos;
using PolvorApp.SharedKernel.Images;

namespace PolvorApp.Api.Tests.Catalog;

/// <summary>Spec "Logo validation and processing" (design D3): the logo rules through the real normaliser.</summary>
public sealed class LogoRulesTests : IDisposable
{
    private readonly SkiaImageNormalizer _normalizer = new(Microsoft.Extensions.Logging.Abstractions.NullLogger<SkiaImageNormalizer>.Instance);

    public void Dispose() => _normalizer.Dispose();

    [Theory]
    [InlineData(255, 200, false)]
    [InlineData(256, 200, true)]
    [InlineData(200, 256, true)]
    public async Task The_long_side_must_be_at_least_256_px(int width, int height, bool accepted) =>
        await AssertOutcomeAsync(TestImages.Png(width, height), accepted, ImageRejection.TooSmall);

    [Theory]
    [InlineData(900, 300, true)]
    [InlineData(903, 300, false)]
    [InlineData(300, 903, false)]
    [InlineData(256, 85, false)]
    public async Task The_long_side_may_be_at_most_three_times_the_short_side(int width, int height, bool accepted) =>
        await AssertOutcomeAsync(TestImages.Png(width, height), accepted, ImageRejection.AspectRatio);

    [Fact]
    public async Task A_large_logo_is_scaled_down_keeping_its_shape()
    {
        var logo = await NormalizeAsync(TestImages.Png(3000, 1500));

        Assert.Equal((1024, 512), (logo.Width, logo.Height));
    }

    [Fact]
    public async Task A_small_logo_is_never_scaled_up()
    {
        var logo = await NormalizeAsync(TestImages.Png(300, 300));

        Assert.Equal((300, 300), (logo.Width, logo.Height));
    }

    [Fact]
    public async Task An_image_over_40_megapixels_is_too_large() =>
        await AssertOutcomeAsync(TestImages.PngHeaderOnly(8000, 6000), accepted: false, ImageRejection.TooLarge);

    [Fact]
    public async Task The_logo_is_stored_as_png_with_its_transparency_and_colours()
    {
        // A dark emblem on the left half, a fully transparent background on the right.
        var input = SyntheticPng.Rgba(600, 400, (x, _) => x < 300 ? ((byte)20, (byte)30, (byte)40, (byte)255) : ((byte)0, (byte)0, (byte)0, (byte)0));

        var logo = await NormalizeAsync(input);

        Assert.Equal(LogoStorage.ContentType, logo.ContentType);
        Assert.True(logo.Content.Span.StartsWith((ReadOnlySpan<byte>)[0x89, (byte)'P', (byte)'N', (byte)'G']));
        Assert.Equal(new SkiaSharp.SKColor(20, 30, 40, 255), TestImages.PixelAt(logo.Content.Span, 100, 200));
        Assert.Equal(0, TestImages.PixelAt(logo.Content.Span, 500, 200).Alpha);
    }

    [Fact]
    public async Task A_jpeg_logo_loses_its_metadata()
    {
        var logo = await NormalizeAsync(TestImages.JpegWithExif(1200, 800));

        var bytes = logo.Content.Span;
        Assert.Equal(LogoStorage.ContentType, logo.ContentType);
        Assert.Equal(-1, bytes.IndexOf("SyntheticCam"u8));
        foreach (var chunk in new[] { "eXIf", "tEXt", "iTXt", "zTXt" })
        {
            Assert.Equal(-1, bytes.IndexOf(Encoding.ASCII.GetBytes(chunk)));
        }
    }

    [Fact]
    public async Task A_logo_photographed_sideways_is_stored_upright()
    {
        // Stored 800 × 600 with EXIF orientation 6: upright it is 600 × 800, its quadrants in place.
        var logo = await NormalizeAsync(TestImages.JpegWithExif(600, 800, orientation: 6));

        Assert.Equal((600, 800), (logo.Width, logo.Height));
        Assert.True(TestImages.Near(TestImages.PixelAt(logo.Content.Span, 50, 50), TestImages.TopLeft));
        Assert.True(TestImages.Near(TestImages.PixelAt(logo.Content.Span, 550, 750), TestImages.BottomRight));
    }

    [Fact]
    public async Task A_webp_logo_is_stored_as_png()
    {
        var logo = await NormalizeAsync(TestImages.Webp(800, 400));

        Assert.Equal((LogoStorage.ContentType, 800, 400), (logo.ContentType, logo.Width, logo.Height));
    }

    [Fact]
    public async Task An_svg_is_not_an_accepted_format()
    {
        var svg = Encoding.UTF8.GetBytes(
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"512\" height=\"512\"><script>alert(1)</script><circle r=\"200\" cx=\"256\" cy=\"256\"/></svg>");

        await AssertOutcomeAsync(svg, accepted: false, ImageRejection.UnsupportedFormat);
    }

    private async Task<NormalizedImage> NormalizeAsync(byte[] input)
    {
        using var stream = new MemoryStream(input);
        var result = await _normalizer.NormalizeAsync(stream, LogoStorage.Rules, TestContext.Current.CancellationToken);
        return Assert.IsType<NormalizedImage>(result);
    }

    private async Task AssertOutcomeAsync(byte[] input, bool accepted, ImageRejection rejection)
    {
        using var stream = new MemoryStream(input);
        var result = await _normalizer.NormalizeAsync(stream, LogoStorage.Rules, TestContext.Current.CancellationToken);
        if (accepted)
        {
            Assert.IsType<NormalizedImage>(result);
        }
        else
        {
            Assert.Equal(rejection, Assert.IsType<RejectedImage>(result).Reason);
        }
    }
}
