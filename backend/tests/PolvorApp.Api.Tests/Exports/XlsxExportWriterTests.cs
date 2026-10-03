using ClosedXML.Excel;
using PolvorApp.Exports.Contracts;
using PolvorApp.Exports.Definitions;
using PolvorApp.Exports.Writers;

namespace PolvorApp.Api.Tests.Exports;

/// <summary>The Excel writer (spec: Excel and PDF; design D4), checked against golden files.</summary>
public sealed class XlsxExportWriterTests
{
    public static TheoryData<string> Cases => GoldenData.Cases;

    [Theory]
    [MemberData(nameof(Cases))]
    public void Each_definition_matches_its_golden_file(string name) =>
        Golden.Matches($"{name}.xlsx", DocumentText.Grid(XlsxExportWriter.Write(GoldenData.Table(name))));

    [Fact]
    public void Text_that_could_run_as_a_formula_is_neutralised()
    {
        var table = new DocumentTable("stem", "Título", [], "versión", [new("Nombre", DocumentCellType.Text), new("Kg", DocumentCellType.Number)],
            [["=1+1", 1], ["@SUM(A1)", 2], ["-3", 3], ["+34", 4], ["'Hola", 5], ["Normal", 6]], null);

        using var workbook = new XLWorkbook(new MemoryStream(XlsxExportWriter.Write(table)));
        var sheet = workbook.Worksheets.Single();
        var values = sheet.Column(1).CellsUsed().Skip(3).Select(c => c.GetString()).ToList();

        Assert.Equal(["'=1+1", "'@SUM(A1)", "'-3", "'+34", "''Hola", "Normal"], values);
        Assert.DoesNotContain(sheet.CellsUsed(), cell => cell.HasFormula);
    }

    [Fact]
    public void A_table_without_rows_keeps_its_headers()
    {
        var table = new DocumentTable("stem", "Título", ["PROVISIONAL"], "versión", [new("Nombre", DocumentCellType.Text)], [], null);

        using var workbook = new XLWorkbook(new MemoryStream(XlsxExportWriter.Write(table)));
        var sheet = workbook.Worksheets.Single();

        Assert.Equal("Nombre", sheet.Cell(5, 1).GetString());
        Assert.False(sheet.Cell(6, 1).HasFormula || !sheet.Cell(6, 1).IsEmpty());
    }
}
