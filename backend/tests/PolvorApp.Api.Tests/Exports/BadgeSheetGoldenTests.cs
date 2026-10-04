using Microsoft.Extensions.Time.Testing;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.Exports.Contracts;
using PolvorApp.Exports.Writers;
using UglyToad.PdfPig;

namespace PolvorApp.Api.Tests.Exports;

/// <summary>
/// Golden files of a synthetic badge sheet (add-badges, design D9): its words line by line and the
/// images per page, with and without the logo, with a missing photo, a pending license and accented
/// and Valencian names. Generated images only; no real data.
/// </summary>
public sealed class BadgeSheetGoldenTests
{
    private static readonly DocumentRenderer Renderer = new(new FakeTimeProvider(new DateTimeOffset(2031, 3, 2, 9, 0, 0, TimeSpan.Zero)));

    private static readonly string[] Labels = ["Apellidos", "Nombre", "DNI/NIE", "Código", "Fecha de caducidad", "Comparsa"];

    [Theory]
    [InlineData(true, "badge-sheet.pdf")]
    [InlineData(false, "badge-sheet-without-logo.pdf")]
    public void The_badge_sheet_matches_its_golden_file(bool logo, string golden)
    {
        var first = Renderer.RenderBadgeSheet(Sheet(logo)).Content.ToArray();
        var second = Renderer.RenderBadgeSheet(Sheet(logo)).Content.ToArray();

        Assert.Equal(first, second);
        Golden.Matches(golden, Text(first));
    }

    private static string Text(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);
        var images = string.Join(", ", document.GetPages().Select(p => $"page {p.Number}: {p.GetImages().Count()} image(s)"));
        return DocumentText.Pdf(pdf) + $"--- images ---\n{images}\n";
    }

    private static DocumentBadgeSheet Sheet(bool logo)
    {
        var photo = DocumentImage.FromJpeg(TestImages.Jpeg(300, 400));
        DocumentBadge[] badges =
        [
            new(photo, ["Sintético Pérez", "Ana María", "00000000T", "1001", "31/05/2033", "Comparsa Sintética Norte"]),
            new(null, ["Sintètic i Col·lell", "Lluís", "X0000000T", "1002", null, "Comparsa Sintética Sur"]),
            new(photo, ["Ejemplo Núñez", "Begoña", "00000001R", "1003", "01/01/2030", "Comparsa Sintética Sur"]),
        ];
        return new DocumentBadgeSheet(
            "polvorapp-badges-selection-11-20310302",
            "Carnets de arcabucero",
            "ARCABUCERO",
            "Unión de Comparsas Sintéticas «Ejemplo»",
            Labels,
            [.. badges, .. Enumerable.Range(4, 8).Select(i => new DocumentBadge(photo, [$"Sintético {i}", "Ana", "00000000T", $"{1000 + i}", "31/05/2033", "Comparsa Sintética Norte"]))],
            logo ? DocumentImage.FromPng(TestImages.Png(200, 240, transparent: true)) : null);
    }
}
