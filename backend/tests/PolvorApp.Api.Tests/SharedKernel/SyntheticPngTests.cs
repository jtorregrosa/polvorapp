using PolvorApp.SharedKernel.Images;
using SkiaSharp;

namespace PolvorApp.Api.Tests.SharedKernel;

/// <summary>
/// The PNG writer the synthetic seeds share (add-comparsa-logos design D9): flat shapes, no image
/// library, readable by the same decoder as uploads.
/// </summary>
public sealed class SyntheticPngTests
{
    [Fact]
    public void An_rgb_image_decodes_to_its_size_and_colours()
    {
        var png = SyntheticPng.Rgb(40, 30, (x, _) => x < 20 ? ((byte)255, (byte)0, (byte)0) : ((byte)0, (byte)0, (byte)255));

        using var bitmap = SKBitmap.Decode(png);

        Assert.Equal((40, 30), (bitmap.Width, bitmap.Height));
        Assert.Equal(SKColors.Red, bitmap.GetPixel(5, 5));
        Assert.Equal(SKColors.Blue, bitmap.GetPixel(35, 25));
    }

    [Fact]
    public void An_rgba_image_keeps_its_transparency()
    {
        var png = SyntheticPng.Rgba(32, 32, (x, _) => x < 16 ? ((byte)20, (byte)20, (byte)20, (byte)255) : ((byte)0, (byte)0, (byte)0, (byte)0));

        using var bitmap = SKBitmap.Decode(png);

        Assert.Equal((32, 32), (bitmap.Width, bitmap.Height));
        Assert.Equal(new SKColor(20, 20, 20, 255), bitmap.GetPixel(4, 4));
        Assert.Equal(0, bitmap.GetPixel(28, 4).Alpha);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(10, -1)]
    public void Empty_or_negative_sizes_are_refused(int width, int height) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => SyntheticPng.Rgb(width, height, (_, _) => (0, 0, 0)));
}
