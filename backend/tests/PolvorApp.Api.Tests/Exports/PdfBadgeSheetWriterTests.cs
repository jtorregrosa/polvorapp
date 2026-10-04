using Microsoft.Extensions.Time.Testing;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.Exports.Contracts;
using PolvorApp.Exports.Writers;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;

namespace PolvorApp.Api.Tests.Exports;

/// <summary>
/// The badge print sheet read back from the PDF's geometry (spec: Badge print sheet; design D2): A4
/// portrait, 2 × 5 ID-1 cards at exact size, crop marks on the cutting lines only, the 3 mm safe area,
/// the photo slot, empty frames and lines, and values printed in full.
/// </summary>
public sealed class PdfBadgeSheetWriterTests
{
    private const double PointsPerMillimetre = 72 / 25.4;
    private const double CardWidth = 85.60;
    private const double CardHeight = 53.98;
    private const double GridLeft = (210 - 2 * CardWidth) / 2;
    private const double GridTop = (297 - 5 * CardHeight) / 2;
    private const double SafeArea = 3;
    private const double Tolerance = 0.05;

    private static readonly DocumentRenderer Renderer = new(new FakeTimeProvider(new DateTimeOffset(2031, 3, 2, 9, 0, 0, TimeSpan.Zero)));

    private static readonly string[] Labels = ["Apellidos", "Nombre", "DNI/NIE", "Código", "Fecha de caducidad", "Comparsa"];

    [Fact]
    public void A_full_sheet_is_a4_portrait_with_ten_cards_at_exact_size()
    {
        using var pdf = Open(Sheet(10));
        var page = pdf.GetPage(1);

        Assert.Equal(1, pdf.NumberOfPages);
        Assert.Equal(210, Millimetres(page.Width), 1);
        Assert.Equal(297, Millimetres(page.Height), 1);

        var bands = Bands(page);
        Assert.Equal(10, bands.Count);
        for (var index = 0; index < 10; index++)
        {
            var (left, top) = CardOrigin(index);
            var band = bands[index];
            Assert.Equal(left, band.Left, Tolerance);
            Assert.Equal(top, band.Top, Tolerance);
            Assert.Equal(CardWidth, band.Width, Tolerance);
        }
    }

    [Fact]
    public void Crop_marks_sit_on_every_cutting_line_outside_the_cards()
    {
        using var pdf = Open(Sheet(10));
        var marks = CropMarks(pdf.GetPage(1));

        var vertical = marks.Where(m => m.Height > m.Width).Select(m => m.Left).ToList();
        var horizontal = marks.Where(m => m.Width > m.Height).Select(m => m.Top).ToList();

        Assert.Equal(6, vertical.Count);
        Assert.Equal(12, horizontal.Count);
        foreach (var column in Enumerable.Range(0, 3))
        {
            Assert.Equal(2, vertical.Count(x => Math.Abs(x - (GridLeft + column * CardWidth)) < Tolerance));
        }

        foreach (var row in Enumerable.Range(0, 6))
        {
            Assert.Equal(2, horizontal.Count(y => Math.Abs(y - (GridTop + row * CardHeight)) < Tolerance));
        }

        Assert.All(marks, mark => Assert.False(Overlaps(mark, GridLeft, GridTop, 2 * CardWidth, 5 * CardHeight)));
    }

    [Fact]
    public void Eleven_badges_take_two_pages_and_the_empty_positions_have_no_marks()
    {
        using var pdf = Open(Sheet(11));
        var second = pdf.GetPage(2);

        Assert.Equal(2, pdf.NumberOfPages);
        var band = Assert.Single(Bands(second));
        Assert.Equal(GridLeft, band.Left, Tolerance);
        Assert.Equal(GridTop, band.Top, Tolerance);

        var marks = CropMarks(second);
        var vertical = marks.Where(m => m.Height > m.Width).Select(m => m.Left).ToList();
        var horizontal = marks.Where(m => m.Width > m.Height).Select(m => m.Top).ToList();
        Assert.Equal(4, vertical.Count);
        Assert.All(vertical, x => Assert.True(x < GridLeft + CardWidth + Tolerance));
        Assert.Equal(4, horizontal.Count);
        Assert.All(horizontal, y => Assert.True(y < GridTop + CardHeight + Tolerance));

        // Every mark sits next to the printed card: above or below its row, left or right of its column.
        Assert.All(marks, mark => Assert.True(mark.Top < GridTop + CardHeight + 8 && mark.Left < GridLeft + CardWidth + 8));
    }

    [Fact]
    public void Long_labels_in_any_language_shrink_inside_the_card()
    {
        string[] labels = ["Cognoms i altres noms de la persona", "Nom complet", "Document nacional d'identitat", "Codi de la Federació", "Data de caducitat de la llicència d'armes", "Comparsa a la qual pertany"];
        var sheet = new DocumentBadgeSheet("stem", "Carnets", "ARCABUSSER", "Unió de Comparses Sintètiques «Exemple»", labels, Sheet(2).Badges, null);
        using var pdf = Open(sheet);
        var page = pdf.GetPage(1);

        Assert.Contains("caducitat", page.Text, StringComparison.Ordinal);
        Assert.All(page.GetWords(), word => Assert.True(InsideSafeArea(Box(word.BoundingBox, page.Height)), $"{word.Text} overflows."));
    }

    [Fact]
    public void Words_photos_and_logos_stay_in_the_safe_area()
    {
        using var pdf = Open(Sheet(10, logo: true));
        var page = pdf.GetPage(1);

        foreach (var word in page.GetWords())
        {
            var box = Box(word.BoundingBox, page.Height);
            Assert.True(InsideSafeArea(box), $"A word lies outside the safe area at {box}.");
        }

        foreach (var image in page.GetImages())
        {
            var box = Box(image.BoundingBox, page.Height);
            Assert.True(InsideSafeArea(box), $"An image lies outside the safe area at {box}.");
        }
    }

    [Fact]
    public void Each_card_has_its_photo_in_the_slot_and_the_logo()
    {
        using var pdf = Open(Sheet(2, logo: true));
        var images = pdf.GetPage(1).GetImages().ToList();

        Assert.Equal(4, images.Count);
        var photos = images.Where(i => i.WidthInSamples == 300).ToList();
        Assert.Equal(2, photos.Count);
        Assert.All(photos, photo =>
        {
            Assert.Equal(400, photo.HeightInSamples);
            var box = Box(photo.BoundingBox, pdf.GetPage(1).Height);
            Assert.Equal(20, box.Width, 0.1);
            Assert.Equal(26.67, box.Height, 0.1);
        });
    }

    [Fact]
    public void A_badge_without_photo_has_an_empty_frame_and_without_value_an_empty_line()
    {
        var badge = new DocumentBadge(null, ["Sintético", "Ana", "00000000T", "123", null, "Comparsa Sintética Norte"]);
        using var pdf = Open(new DocumentBadgeSheet("stem", "Carnets", "ARCABUCERO", "Federación Sintética", Labels, [badge], null));
        var page = pdf.GetPage(1);

        Assert.Empty(page.GetImages());
        var frames = page.Paths.Where(p => p.IsStroked).Select(p => Box(p.GetBoundingRectangle()!.Value, page.Height))
            .Where(b => Math.Abs(b.Width - 20) < 0.3 && Math.Abs(b.Height - 26.67) < 0.3).ToList();
        Assert.Single(frames);
        var text = page.Text;
        Assert.Contains("Fecha de caducidad", text, StringComparison.Ordinal);
        Assert.DoesNotContain("31/05/2033", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_surname_of_100_characters_is_printed_in_full()
    {
        var surname = string.Concat(Enumerable.Repeat("Sintético Ejemplar ", 6))[..100].TrimEnd();
        var badge = new DocumentBadge(null, [surname, "Ana", "00000000T", "123", "31/05/2033", "Comparsa Sintética Norte"]);
        using var pdf = Open(new DocumentBadgeSheet("stem", "Carnets", "ARCABUCERO", "Federación Sintética", Labels, [badge], null));
        var page = pdf.GetPage(1);

        var letters = string.Concat(page.Letters.Select(l => l.Value)).Replace(" ", "", StringComparison.Ordinal);
        Assert.Contains(surname.Replace(" ", "", StringComparison.Ordinal), letters, StringComparison.Ordinal);
        Assert.Contains("Ana", letters, StringComparison.Ordinal);
        Assert.All(page.GetWords(), word => Assert.True(InsideSafeArea(Box(word.BoundingBox, page.Height))));
    }

    [Fact]
    public void The_metadata_holds_the_title_only_and_the_output_is_deterministic()
    {
        var sheet = Sheet(3, logo: true);
        var first = Renderer.RenderBadgeSheet(sheet);
        var second = Renderer.RenderBadgeSheet(sheet);

        Assert.True(first.Content.Span.SequenceEqual(second.Content.Span));
        Assert.Equal("application/pdf", first.ContentType);
        Assert.Equal("polvorapp-badges-selection-3-20310302.pdf", first.FileName);
        using var pdf = PdfDocument.Open(first.Content.ToArray());
        var information = pdf.Information;
        Assert.Equal("Carnets de arcabucero", information.Title);
        Assert.Equal("PolvorApp", information.Author);
        Assert.Equal("PolvorApp", information.Creator);
        Assert.Equal("PolvorApp", information.Producer);
        Assert.True(string.IsNullOrEmpty(information.Subject));
        Assert.True(string.IsNullOrEmpty(information.Keywords));
    }

    internal static DocumentBadgeSheet Sheet(int count, bool logo = false) => new(
        $"polvorapp-badges-selection-{count}-20310302",
        "Carnets de arcabucero",
        "ARCABUCERO",
        "Unión de Comparsas Sintéticas «Ejemplo»",
        Labels,
        [.. Enumerable.Range(1, count).Select(i => new DocumentBadge(
            DocumentImage.FromJpeg(TestImages.Jpeg(300, 400)),
            [$"Sintético Ejemplo {i}", "Ana María", "00000000T", $"{1000 + i}", "31/05/2033", "Comparsa Sintética Norte"]))],
        logo ? DocumentImage.FromPng(TestImages.Png(200, 200, transparent: true)) : null);

    private static PdfDocument Open(DocumentBadgeSheet sheet) => PdfDocument.Open(Renderer.RenderBadgeSheet(sheet).Content.ToArray());

    private static double Millimetres(double points) => points / PointsPerMillimetre;

    private static (double Left, double Top) CardOrigin(int index) => (GridLeft + index % 2 * CardWidth, GridTop + index / 2 * CardHeight);

    /// <summary>A rectangle in millimetres from the page's top-left corner.</summary>
    private static MmBox Box(PdfRectangle rectangle, double pageHeight) => new(
        Millimetres(rectangle.Left), Millimetres(pageHeight - rectangle.Top), Millimetres(rectangle.Width), Millimetres(rectangle.Height));

    /// <summary>The green header bands: filled rectangles as wide as a card, top to bottom and left to right.</summary>
    private static List<MmBox> Bands(Page page) => [.. page.Paths
        .Where(p => p.IsFilled && p.GetBoundingRectangle() is not null)
        .Select(p => Box(p.GetBoundingRectangle()!.Value, page.Height))
        .Where(b => Math.Abs(b.Width - CardWidth) < 0.5 && b.Height is > 8 and < 15)
        .OrderBy(b => Math.Round(b.Top)).ThenBy(b => b.Left)];

    /// <summary>
    /// Short stroked lines in the page margins. PdfPig also lists each stroked path once more with a
    /// zero line width; those copies are skipped.
    /// </summary>
    private static List<MmBox> CropMarks(Page page) => [.. page.Paths
        .Where(p => p.IsStroked && p.LineWidth > 0 && p.GetBoundingRectangle() is not null)
        .Select(p => Box(p.GetBoundingRectangle()!.Value, page.Height))
        .Where(b => Math.Max(b.Width, b.Height) is > 4 and < 6 && Math.Min(b.Width, b.Height) < 0.5)];

    private static bool Overlaps(MmBox box, double left, double top, double width, double height) =>
        box.Left < left + width - Tolerance && box.Left + box.Width > left + Tolerance
        && box.Top < top + height - Tolerance && box.Top + box.Height > top + Tolerance;

    /// <summary>Whether the box lies inside one card's area inset by the safe area.</summary>
    private static bool InsideSafeArea(MmBox box)
    {
        var column = (int)Math.Floor((box.Left - GridLeft) / CardWidth);
        var row = (int)Math.Floor((box.Top - GridTop) / CardHeight);
        var left = GridLeft + column * CardWidth + SafeArea - Tolerance;
        var top = GridTop + row * CardHeight + SafeArea - Tolerance;
        return box.Left >= left && box.Top >= top
            && box.Left + box.Width <= left + CardWidth - 2 * SafeArea + 2 * Tolerance
            && box.Top + box.Height <= top + CardHeight - 2 * SafeArea + 2 * Tolerance;
    }

    private readonly record struct MmBox(double Left, double Top, double Width, double Height);
}
