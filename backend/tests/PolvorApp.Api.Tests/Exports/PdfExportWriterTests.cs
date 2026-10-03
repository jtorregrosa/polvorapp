using PolvorApp.Exports.Contracts;
using PolvorApp.Exports.Definitions;
using PolvorApp.Exports.Writers;
using UglyToad.PdfPig;

namespace PolvorApp.Api.Tests.Exports;

/// <summary>The PDF writer (spec: Excel and PDF; design D4), checked against golden files through PdfPig.</summary>
public sealed class PdfExportWriterTests
{
    private static readonly DateOnly GeneratedOn = new(2031, 2, 15);

    public static TheoryData<string> Cases => GoldenData.Cases;

    [Theory]
    [MemberData(nameof(Cases))]
    public void Each_definition_matches_its_golden_file(string name) =>
        Golden.Matches($"{name}.pdf", DocumentText.Pdf(PdfExportWriter.Write(GoldenData.Table(name), GeneratedOn)));

    [Theory]
    [InlineData("rental-company", false)]
    [InlineData("comparsa-list", true)]
    [InlineData("arms-authority", true)]
    public void Pages_are_landscape_above_six_columns(string name, bool landscape)
    {
        using var pdf = PdfDocument.Open(PdfExportWriter.Write(GoldenData.Table(name), GeneratedOn));
        var page = pdf.GetPage(1);

        Assert.Equal(landscape, page.Width > page.Height);
    }

    [Fact]
    public void A_long_table_repeats_its_header_and_numbers_its_pages()
    {
        var rows = Enumerable.Range(1, 120).Select(i => (IReadOnlyList<object?>)[$"Sintético {i:000}, Arcabucero", i]).ToList();
        var table = new DocumentTable("stem", "Título largo", ["PROVISIONAL"], "Definición prueba, versión 1",
            [new("Apellidos y nombre", DocumentCellType.Text), new("Pólvora (kg)", DocumentCellType.Number)], rows, null);

        using var pdf = PdfDocument.Open(PdfExportWriter.Write(table, GeneratedOn));

        Assert.True(pdf.NumberOfPages >= 2);
        foreach (var page in pdf.GetPages())
        {
            var text = string.Join("\n", DocumentText.Lines(page));
            Assert.Contains("Apellidos y nombre", text, StringComparison.Ordinal);
            Assert.Contains($"15/02/2031 · {page.Number} / {pdf.NumberOfPages}", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_table_without_rows_keeps_its_headers()
    {
        var table = new DocumentTable("stem", "Título", [], "versión", [new("Apellidos y nombre", DocumentCellType.Text)], [], null);

        using var pdf = PdfDocument.Open(PdfExportWriter.Write(table, GeneratedOn));

        Assert.Contains("Apellidos y nombre", string.Join("\n", DocumentText.Lines(pdf.GetPage(1))), StringComparison.Ordinal);
    }

    [Fact]
    public void Names_with_spanish_valencian_and_other_latin_letters_render()
    {
        const string Names = "Àngela Çàrcer Ñúñez Pérez-Lloréns Ŀl·lorenç Ĳssel Şerban Țăran Łucja Ødegård Ğül";
        var table = new DocumentTable("stem", "Título", [], "versión", [new("Apellidos y nombre", DocumentCellType.Text)], [[Names]], null);

        using var pdf = PdfDocument.Open(PdfExportWriter.Write(table, GeneratedOn));

        Assert.Contains("Ñúñez", string.Join(" ", DocumentText.Lines(pdf.GetPage(1))), StringComparison.Ordinal);
    }

    [Fact]
    public void The_same_table_on_the_same_day_gives_the_same_file() =>
        Assert.Equal(
            PdfExportWriter.Write(GoldenData.Table("arms-authority"), GeneratedOn),
            PdfExportWriter.Write(GoldenData.Table("arms-authority"), GeneratedOn));
}
