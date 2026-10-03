using System.Globalization;
using System.Text;
using ClosedXML.Excel;
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
        Golden.Matches($"{name}.xlsx", Grid(XlsxExportWriter.Write(GoldenData.Table(name))));

    [Fact]
    public void Text_that_could_run_as_a_formula_is_neutralised()
    {
        var table = new ExportTable("stem", "Título", [], "versión", [new("Nombre", ExportCellType.Text), new("Kg", ExportCellType.Integer)],
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
        var table = new ExportTable("stem", "Título", ["PROVISIONAL"], "versión", [new("Nombre", ExportCellType.Text)], [], null);

        using var workbook = new XLWorkbook(new MemoryStream(XlsxExportWriter.Write(table)));
        var sheet = workbook.Worksheets.Single();

        Assert.Equal("Nombre", sheet.Cell(5, 1).GetString());
        Assert.False(sheet.Cell(6, 1).HasFormula || !sheet.Cell(6, 1).IsEmpty());
    }

    /// <summary>
    /// The workbook as text: one line per used cell with its address, type and number format, then
    /// the merged ranges, the frozen rows and the auto-filter.
    /// </summary>
    private static string Grid(byte[] bytes)
    {
        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var sheet = Assert.Single(workbook.Worksheets);
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"sheet: {sheet.Name}");
        foreach (var cell in sheet.CellsUsed().OrderBy(c => c.Address.RowNumber).ThenBy(c => c.Address.ColumnNumber))
        {
            var value = cell.DataType switch
            {
                XLDataType.DateTime => cell.GetDateTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                XLDataType.Number => cell.GetDouble().ToString(CultureInfo.InvariantCulture),
                _ => cell.GetString(),
            };
            var format = string.IsNullOrEmpty(cell.Style.NumberFormat.Format) ? "" : $" [{cell.Style.NumberFormat.Format}]";
            var bold = cell.Style.Font.Bold ? " bold" : "";
            text.AppendLine(CultureInfo.InvariantCulture, $"{cell.Address}: {cell.DataType}{format}{bold} {value}");
        }

        text.AppendLine(CultureInfo.InvariantCulture, $"merged: {string.Join(", ", sheet.MergedRanges.Select(r => r.RangeAddress.ToString()))}");
        text.AppendLine(CultureInfo.InvariantCulture, $"frozen rows: {sheet.SheetView.SplitRow}");
        text.AppendLine(CultureInfo.InvariantCulture, $"auto-filter: {(sheet.AutoFilter.IsEnabled ? sheet.AutoFilter.Range.RangeAddress.ToString() : "none")}");
        return text.ToString();
    }
}
