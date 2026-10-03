using System.Globalization;
using System.Text;
using PolvorApp.Exports.Definitions;
using PolvorApp.Exports.Writers;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace PolvorApp.Api.Tests.Exports;

/// <summary>The PDF writer (spec: Excel and PDF; design D4), checked against golden files through PdfPig.</summary>
public sealed class PdfExportWriterTests
{
    private static readonly DateOnly GeneratedOn = new(2031, 2, 15);

    public static TheoryData<string> Cases => GoldenData.Cases;

    [Theory]
    [MemberData(nameof(Cases))]
    public void Each_definition_matches_its_golden_file(string name) =>
        Golden.Matches($"{name}.pdf", Text(PdfExportWriter.Write(GoldenData.Table(name), GeneratedOn)));

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
        var table = new ExportTable("stem", "Título largo", ["PROVISIONAL"], "Definición prueba, versión 1",
            [new("Apellidos y nombre", ExportCellType.Text), new("Pólvora (kg)", ExportCellType.Integer)], rows, null);

        using var pdf = PdfDocument.Open(PdfExportWriter.Write(table, GeneratedOn));

        Assert.True(pdf.NumberOfPages >= 2);
        foreach (var page in pdf.GetPages())
        {
            var text = string.Join("\n", Lines(page));
            Assert.Contains("Apellidos y nombre", text, StringComparison.Ordinal);
            Assert.Contains($"15/02/2031 · {page.Number} / {pdf.NumberOfPages}", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_table_without_rows_keeps_its_headers()
    {
        var table = new ExportTable("stem", "Título", [], "versión", [new("Apellidos y nombre", ExportCellType.Text)], [], null);

        using var pdf = PdfDocument.Open(PdfExportWriter.Write(table, GeneratedOn));

        Assert.Contains("Apellidos y nombre", string.Join("\n", Lines(pdf.GetPage(1))), StringComparison.Ordinal);
    }

    [Fact]
    public void Names_with_spanish_valencian_and_other_latin_letters_render()
    {
        const string Names = "Àngela Çàrcer Ñúñez Pérez-Lloréns Ŀl·lorenç Ĳssel Şerban Țăran Łucja Ødegård Ğül";
        var table = new ExportTable("stem", "Título", [], "versión", [new("Apellidos y nombre", ExportCellType.Text)], [[Names]], null);

        using var pdf = PdfDocument.Open(PdfExportWriter.Write(table, GeneratedOn));

        Assert.Contains("Ñúñez", string.Join(" ", Lines(pdf.GetPage(1))), StringComparison.Ordinal);
    }

    [Fact]
    public void The_same_table_on_the_same_day_gives_the_same_file() =>
        Assert.Equal(
            PdfExportWriter.Write(GoldenData.Table("arms-authority"), GeneratedOn),
            PdfExportWriter.Write(GoldenData.Table("arms-authority"), GeneratedOn));

    /// <summary>Each page's words grouped into lines by baseline, top to bottom and left to right.</summary>
    private static string Text(byte[] bytes)
    {
        using var pdf = PdfDocument.Open(bytes);
        var text = new StringBuilder();
        foreach (var page in pdf.GetPages())
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"--- page {page.Number} ({(page.Width > page.Height ? "landscape" : "portrait")}) ---");
            foreach (var line in Lines(page))
            {
                text.AppendLine(line);
            }
        }

        return text.ToString();
    }

    /// <summary>Words whose baselines are within 2 pt belong to one line, so rounding never splits a line.</summary>
    private static IEnumerable<string> Lines(Page page)
    {
        var lines = new List<List<Word>>();
        foreach (var word in page.GetWords().OrderByDescending(w => w.BoundingBox.Bottom))
        {
            if (lines.Count > 0 && Math.Abs(lines[^1][0].BoundingBox.Bottom - word.BoundingBox.Bottom) <= 2)
            {
                lines[^1].Add(word);
            }
            else
            {
                lines.Add([word]);
            }
        }

        return lines.Select(line => string.Join(" ", line.OrderBy(w => w.BoundingBox.Left).Select(w => w.Text)));
    }
}
