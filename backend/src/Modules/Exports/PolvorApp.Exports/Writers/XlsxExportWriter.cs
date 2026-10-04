using ClosedXML.Excel;
using PolvorApp.Exports.Contracts;
using PolvorApp.Exports.Definitions;

namespace PolvorApp.Exports.Writers;

/// <summary>
/// Writes a <see cref="DocumentTable"/> as one worksheet (spec: Excel and PDF; design D4): the title,
/// the notices and the version line, then the table with a bold, frozen, filterable header row, typed
/// cells and a bold total row. Every text cell is a string, never a formula, and text that a
/// spreadsheet could run as a formula when re-saved (a leading <c>=</c>, <c>+</c>, <c>-</c>,
/// <c>@</c>, tab or carriage return) is prefixed with an apostrophe; so is text that already starts
/// with one, which ClosedXML would otherwise swallow.
/// </summary>
internal static class XlsxExportWriter
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public const string Extension = "xlsx";

    private const string SheetName = "PolvorApp";
    private const string DateFormat = "dd/mm/yyyy";
    private const string WholeNumberFormat = "0";
    private const int MaxColumnWidth = 48;

    public static byte[] Write(DocumentTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(SheetName);
        var columns = Math.Max(1, table.Columns.Count);

        var row = 1;
        var title = sheet.Cell(row, 1);
        title.Value = Text(table.Title);
        title.Style.Font.Bold = true;
        title.Style.Font.FontSize = 14;
        foreach (var notice in table.Notices)
        {
            row++;
            var cell = sheet.Cell(row, 1);
            cell.Value = Text(notice);
            cell.Style.Font.Bold = true;
            sheet.Range(row, 1, row, columns).Merge();
        }

        row++;
        sheet.Cell(row, 1).Value = Text(table.VersionLine);
        sheet.Cell(row, 1).Style.Font.Italic = true;

        row += 2;
        var headerRow = row;
        for (var c = 0; c < table.Columns.Count; c++)
        {
            var header = sheet.Cell(headerRow, c + 1);
            header.Value = Text(table.Columns[c].Header);
            header.Style.Font.Bold = true;
            header.Style.Fill.BackgroundColor = XLColor.FromHtml("#EDEDED");
            header.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
            sheet.Column(c + 1).Width = Width(table.Columns[c]);
        }

        foreach (var cells in table.Rows)
        {
            row++;
            WriteRow(sheet, row, table.Columns, cells, bold: false);
        }

        var lastDataRow = row;
        if (table.TotalRow is { } total)
        {
            row++;
            WriteRow(sheet, row, table.Columns, total, bold: true);
        }

        sheet.SheetView.FreezeRows(headerRow);
        if (table.Columns.Count > 0)
        {
            sheet.Range(headerRow, 1, lastDataRow, table.Columns.Count).SetAutoFilter();
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void WriteRow(IXLWorksheet sheet, int row, IReadOnlyList<DocumentColumn> columns, IReadOnlyList<object?> cells, bool bold)
    {
        for (var c = 0; c < cells.Count; c++)
        {
            var cell = sheet.Cell(row, c + 1);
            switch (cells[c])
            {
                case null:
                    break;
                case int number:
                    cell.Value = number;
                    cell.Style.NumberFormat.Format = WholeNumberFormat;
                    break;
                case DateOnly date:
                    cell.Value = date.ToDateTime(TimeOnly.MinValue);
                    cell.Style.NumberFormat.Format = DateFormat;
                    break;
                case string text:
                    cell.Value = Text(text);
                    break;
                default:
                    throw new InvalidOperationException($"A cell of the export column '{columns[c].Header}' has an unsupported type.");
            }

            cell.Style.Font.Bold = bold;
        }
    }

    /// <summary>
    /// A string cell value, neutralised against formula injection (OWASP CSV injection): a visible
    /// apostrophe stays in front of a dangerous first character, so the text cannot run even after the
    /// file is re-saved as CSV. ClosedXML turns one leading apostrophe into Excel's hidden quote
    /// prefix, so two are written and one remains in the value.
    /// </summary>
    private static XLCellValue Text(string text) => Neutralised(text);

    /// <summary>The neutralised value of a text cell, shared with <see cref="XlsxWorkbookWriter"/>.</summary>
    internal static string Neutralised(string text) =>
        text.Length > 0 && text[0] is '=' or '+' or '-' or '@' or '\t' or '\r' or '\'' ? "''" + text : text;

    private static double Width(DocumentColumn column)
    {
        var byType = column.Type switch
        {
            DocumentCellType.Number => 12,
            DocumentCellType.Date => 13,
            _ => 28,
        };
        return Math.Min(MaxColumnWidth, Math.Max(byType, column.Header.Length + 2));
    }
}
