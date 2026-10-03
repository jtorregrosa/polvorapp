using Microsoft.Extensions.Time.Testing;
using PolvorApp.Exports.Contracts;
using PolvorApp.Exports.Writers;
using UglyToad.PdfPig;

namespace PolvorApp.Api.Tests.Exports;

/// <summary>
/// The document rendering contract other modules use (add-distribution-planning, design D3): tables
/// as Excel and PDF with the export writers, and columns filled in by hand.
/// </summary>
public sealed class DocumentRendererTests
{
    // 23:30 UTC on 14 February is already 15 February in Madrid: the date printed is the Federation's.
    private static readonly FakeTimeProvider Time = new(new DateTimeOffset(2031, 2, 14, 23, 30, 0, TimeSpan.Zero));

    private static readonly DocumentRenderer Renderer = new(Time);

    [Fact]
    public void A_table_renders_as_excel_with_its_file_name()
    {
        var document = Renderer.RenderTable(HandwritingTable(), DocumentFileFormat.Xlsx);

        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", document.ContentType);
        Assert.Equal("polvorapp-2031-test-list.xlsx", document.FileName);
        Golden.Matches("handwriting-table.xlsx", DocumentText.Grid(document.Content.ToArray()));
    }

    [Fact]
    public void A_table_renders_as_pdf_with_its_file_name_and_the_federation_date()
    {
        var document = Renderer.RenderTable(HandwritingTable(), DocumentFileFormat.Pdf);

        Assert.Equal("application/pdf", document.ContentType);
        Assert.Equal("polvorapp-2031-test-list.pdf", document.FileName);
        Golden.Matches("handwriting-table.pdf", DocumentText.Pdf(document.Content.ToArray()));
    }

    [Fact]
    public void Handwriting_columns_have_a_fixed_width_in_the_pdf()
    {
        var shortName = Renderer.RenderTable(HandwritingTable("Ana"), DocumentFileFormat.Pdf);
        var longName = Renderer.RenderTable(HandwritingTable("Arcabucera Sintética de Nombre Muy Largo"), DocumentFileFormat.Pdf);

        Assert.Equal(HandwritingSpacing(shortName), HandwritingSpacing(longName), 1);
        Assert.Equal(PdfExportWriter.HandwritingColumnWidth, HandwritingSpacing(shortName), 1);
    }

    [Fact]
    public void Rows_of_a_table_with_handwriting_columns_are_tall_enough_to_write()
    {
        var withHandwriting = RowSpacing(Renderer.RenderTable(HandwritingTable(), DocumentFileFormat.Pdf));
        var without = RowSpacing(Renderer.RenderTable(PlainTable(), DocumentFileFormat.Pdf));

        Assert.True(withHandwriting >= PdfExportWriter.HandwritingRowHeight, $"Row spacing {withHandwriting} is under the writing height.");
        Assert.True(without < PdfExportWriter.HandwritingRowHeight, $"Plain rows ({without}) should stay compact.");
    }

    [Fact]
    public void The_same_table_on_the_same_day_gives_the_same_file() =>
        Assert.Equal(
            Renderer.RenderTable(HandwritingTable(), DocumentFileFormat.Pdf).Content.ToArray(),
            Renderer.RenderTable(HandwritingTable(), DocumentFileFormat.Pdf).Content.ToArray());

    [Fact]
    public void A_rendered_document_never_prints_its_content() =>
        Assert.Equal("RenderedDocument", Renderer.RenderTable(PlainTable(), DocumentFileFormat.Xlsx).ToString());

    /// <summary>Spec "Distribution lists": an edition without validated orders gives a list with its headings and no rows.</summary>
    [Theory]
    [InlineData(DocumentFileFormat.Xlsx)]
    [InlineData(DocumentFileFormat.Pdf)]
    public void A_table_without_rows_renders_with_its_headings(DocumentFileFormat format)
    {
        var empty = new DocumentTable(
            "polvorapp-2031-test-list",
            "Listado de prueba",
            ["Numeración válida para esta impresión."],
            "test-list, versión 1",
            [new("Nº", DocumentCellType.Number), new("Apellidos y nombre", DocumentCellType.Text), new("Firma", DocumentCellType.Text, ForHandwriting: true)],
            [],
            null);

        var document = Renderer.RenderTable(empty, format);

        var bytes = document.Content.ToArray();
        var text = format == DocumentFileFormat.Pdf ? DocumentText.Pdf(bytes) : DocumentText.Grid(bytes);
        Assert.Contains("Apellidos y nombre", text, StringComparison.Ordinal);
        Assert.Contains("Listado de prueba", text, StringComparison.Ordinal);
        if (format == DocumentFileFormat.Pdf)
        {
            using var pdf = PdfDocument.Open(bytes);
            Assert.Equal(1, pdf.NumberOfPages);
        }
    }

    private static DocumentTable HandwritingTable(string firstName = "Ana") => new(
        "polvorapp-2031-test-list",
        "Listado de prueba",
        ["Numeración válida para esta impresión."],
        "test-list, versión 1",
        [
            new("Nº", DocumentCellType.Number),
            new("Apellidos y nombre", DocumentCellType.Text),
            new("Firma A", DocumentCellType.Text, ForHandwriting: true),
            new("Firma B", DocumentCellType.Text, ForHandwriting: true),
        ],
        [[1, $"Abad Sintético, {firstName}", null, null], [2, "Zamora Sintética, Berta", null, null], [3, "Climent Sintético, Carles", null, null]],
        null);

    private static DocumentTable PlainTable() => new(
        "polvorapp-2031-plain",
        "Listado",
        [],
        "plain, versión 1",
        [new("Nº", DocumentCellType.Number), new("Apellidos y nombre", DocumentCellType.Text)],
        [[1, "Abad Sintético, Ana"], [2, "Zamora Sintética, Berta"], [3, "Climent Sintético, Carles"]],
        null);

    /// <summary>The distance between the left edges of the two handwriting headers: the first one's width.</summary>
    private static double HandwritingSpacing(RenderedDocument document)
    {
        using var pdf = PdfDocument.Open(document.Content.ToArray());
        var words = pdf.GetPage(1).GetWords().ToList();
        var a = words.Single(w => w.Text == "A").BoundingBox.Left;
        var b = words.Single(w => w.Text == "B").BoundingBox.Left;
        return b - a;
    }

    /// <summary>The distance between the baselines of the first two data rows.</summary>
    private static double RowSpacing(RenderedDocument document)
    {
        using var pdf = PdfDocument.Open(document.Content.ToArray());
        var words = pdf.GetPage(1).GetWords().ToList();
        var first = words.First(w => w.Text == "Abad").BoundingBox.Bottom;
        var second = words.First(w => w.Text == "Zamora").BoundingBox.Bottom;
        return first - second;
    }
}
