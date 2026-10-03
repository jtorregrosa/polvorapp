using PolvorApp.Exports.Writers;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using UglyToad.PdfPig;

namespace PolvorApp.Api.Tests.Exports;

/// <summary>QuestPDF renders with the embedded fonts only (design D4).</summary>
public sealed class PdfSetupTests
{
    [Fact]
    public void A_one_page_document_renders_with_the_embedded_font()
    {
        PdfSetup.EnsureApplied();

        var bytes = Document.Create(container => container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.DefaultTextStyle(style => style.FontFamily(PdfSetup.FontFamily));
            page.Content().Text("Pólvora y cañón: ñ, ç, à, ·");
        })).GeneratePdf();

        using var pdf = PdfDocument.Open(bytes);
        Assert.Equal(1, pdf.NumberOfPages);
        Assert.Contains("Pólvora", string.Concat(pdf.GetPage(1).GetWords().Select(w => w.Text + " ")), StringComparison.Ordinal);
        Assert.Contains(pdf.GetPage(1).Letters, letter => letter.FontName?.Contains("Geist", StringComparison.OrdinalIgnoreCase) == true);
    }
}
