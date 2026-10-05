using Microsoft.Extensions.Logging.Abstractions;
using PolvorApp.Api.Platform.Images;
using PolvorApp.ArquebusierRegistry.Photos;
using PolvorApp.SharedKernel.Images;
using PolvorApp.SharedKernel.Seeding;
using SkiaSharp;

namespace PolvorApp.Api.Tests.Images;

/// <summary>
/// The seed's specimen license cards (realistic-seed-data, design D6; spec: Synthetic registry data):
/// the arquebusier's own data on the arms-license layout, watermarked, at the size the photo rules
/// accept, drawn with the embedded font only.
/// </summary>
public sealed class SpecimenCardPainterTests : IDisposable
{
    private static readonly SpecimenCardData Card = new(
        "99000001R", "Vicent", "Sempere Llorens", new DateOnly(1980, 3, 14),
        SyntheticLicenseType.Ae, new DateOnly(2025, 10, 4), new DateOnly(2030, 10, 4));

    private readonly SkiaSpecimenCardPainter _painter = new();

    public void Dispose() => _painter.Dispose();

    [Fact]
    public void The_front_shows_the_holders_data_and_the_watermark()
    {
        var texts = SpecimenCardLayout.Front(Card).Select(t => t.Text).ToList();

        Assert.Contains("LICENCIA DE ARMAS", texts);
        Assert.Contains("99000001R", texts);
        Assert.Contains("VICENT SEMPERE LLORENS", texts);
        Assert.Contains("14-03-1980", texts);
        Assert.Contains("04-10-2025", texts);
        Assert.Contains(SpecimenCardLayout.Watermark, texts);
        Assert.Contains(SpecimenCardLayout.Stamp, texts);
    }

    [Theory]
    [InlineData(SyntheticLicenseType.Ae, "AE")]
    [InlineData(SyntheticLicenseType.AProf, "A-PROF")]
    public void The_back_shows_the_type_and_validity_and_the_watermark(SyntheticLicenseType type, string code)
    {
        var texts = SpecimenCardLayout.Back(Card with { Type = type }).Select(t => t.Text).ToList();

        Assert.Contains(code, texts);
        Assert.Contains("04-10-2025", texts);
        Assert.Contains("04-10-2030", texts);
        Assert.Contains("1) Tipo de licencia", texts);
        Assert.Contains("5) Observaciones", texts);
        Assert.Contains(SpecimenCardLayout.Watermark, texts);
    }

    [Fact]
    public void No_official_wording_is_drawn()
    {
        var texts = SpecimenCardLayout.Front(Card).Concat(SpecimenCardLayout.Back(Card)).Select(t => t.Text).ToList();

        Assert.DoesNotContain(texts, t => t.Contains("Ministerio", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(texts, t => t.Contains("España", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Both_sides_are_1000_by_630_pngs()
    {
        foreach (var png in new[] { _painter.Front(Card), _painter.Back(Card) })
        {
            Assert.Equal([0x89, 0x50, 0x4E, 0x47], png.Take(4));
            using var bitmap = SKBitmap.Decode(png);
            Assert.Equal((1000, 630), (bitmap.Width, bitmap.Height));
        }
    }

    [Fact]
    public async Task Both_sides_pass_the_license_photo_rules()
    {
        using var normalizer = new SkiaImageNormalizer(NullLogger<SkiaImageNormalizer>.Instance);
        foreach (var png in new[] { _painter.Front(Card), _painter.Back(Card) })
        {
            using var stream = new MemoryStream(png);

            var result = await normalizer.NormalizeAsync(stream, PhotoStorage.LicensePhoto, TestContext.Current.CancellationToken);

            Assert.IsType<NormalizedImage>(result);
        }
    }

    [Fact]
    public void A_long_name_still_fits_the_card()
    {
        var card = Card with { FirstName = "Maria Josep", LastName = "Carratalá Marhuenda" };

        var name = SpecimenCardLayout.Front(card).Single(t => t.Text == "MARIA JOSEP CARRATALÁ MARHUENDA");

        Assert.True(name.MaxWidth > 0);
    }

    [Fact]
    public void A_name_too_long_for_the_card_fails_instead_of_printing_unreadably()
    {
        var card = Card with { LastName = string.Join(' ', Enumerable.Repeat("Carratalá Marhuenda", 12)) };

        Assert.Throws<InvalidOperationException>(() => _painter.Front(card));
    }

    [Fact]
    public void A_letter_the_font_lacks_fails()
    {
        var card = Card with { FirstName = "名前" };

        Assert.Throws<InvalidOperationException>(() => _painter.Front(card));
    }

    [Fact]
    public void Drawing_text_uses_the_embedded_font_and_draws_ink()
    {
        var png = _painter.Front(Card);

        using var bitmap = SKBitmap.Decode(png);
        var dark = 0;
        for (var x = 60; x < 600; x += 2)
        {
            if (bitmap.GetPixel(x, 262).Red < 90)
            {
                dark++;
            }
        }

        Assert.True(dark > 10, "the national ID row has no visible ink");
    }
}
