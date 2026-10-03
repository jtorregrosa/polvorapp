using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace PolvorApp.Api.Tests.Exports;

/// <summary>Generated documents read back as text, for golden files (ADR-0008).</summary>
internal static class DocumentText
{
    /// <summary>
    /// The workbook as text: one line per used cell with its address, type and number format, then
    /// the merged ranges, the frozen rows and the auto-filter.
    /// </summary>
    public static string Grid(byte[] bytes)
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

    /// <summary>Each page's words grouped into lines by baseline, top to bottom and left to right.</summary>
    public static string Pdf(byte[] bytes)
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
    public static IEnumerable<string> Lines(Page page)
    {
        ArgumentNullException.ThrowIfNull(page);
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
