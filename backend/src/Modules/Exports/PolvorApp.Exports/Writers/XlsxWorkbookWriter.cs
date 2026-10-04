using ClosedXML.Excel;
using PolvorApp.Exports.Contracts;
using PolvorApp.SharedKernel.Time;

namespace PolvorApp.Exports.Writers;

/// <summary>
/// Writes a <see cref="DocumentWorkbook"/> (add-audit-privacy, design D11): one worksheet per sheet with a
/// bold, frozen header row and typed cells. Text is neutralised against formula injection exactly as
/// in <see cref="XlsxExportWriter"/>; instants are shown in Europe/Madrid.
/// </summary>
internal static class XlsxWorkbookWriter
{
    public const int MaxSheetNameLength = 31;

    private const string DateFormat = "dd/mm/yyyy";
    private const string DateTimeFormat = "dd/mm/yyyy hh:mm";
    private const string WholeNumberFormat = "0";

    public static byte[] Write(DocumentWorkbook workbook)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        using var document = new XLWorkbook();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sheet in workbook.Sheets)
        {
            var worksheet = document.Worksheets.Add(SheetName(sheet.Name, names));
            for (var c = 0; c < sheet.Headers.Count; c++)
            {
                var header = worksheet.Cell(1, c + 1);
                header.Value = XlsxExportWriter.Neutralised(sheet.Headers[c]);
                header.Style.Font.Bold = true;
                header.Style.Fill.BackgroundColor = XLColor.FromHtml("#EDEDED");
            }

            for (var r = 0; r < sheet.Rows.Count; r++)
            {
                var cells = sheet.Rows[r];
                for (var c = 0; c < cells.Count; c++)
                {
                    Write(worksheet.Cell(r + 2, c + 1), cells[c]);
                }
            }

            worksheet.SheetView.FreezeRows(1);
            worksheet.Columns(1, Math.Max(1, sheet.Headers.Count)).Width = 24;
        }

        using var stream = new MemoryStream();
        document.SaveAs(stream);
        return stream.ToArray();
    }

    /// <summary>A valid, unique sheet name: no <c>[]:*?/\</c>, at most 31 characters, numbered when taken.</summary>
    private static string SheetName(string name, HashSet<string> taken)
    {
        var clean = new string([.. name.Select(c => c is '[' or ']' or ':' or '*' or '?' or '/' or '\\' ? ' ' : c)]).Trim();
        clean = clean.Length == 0 ? "Hoja" : clean;
        var candidate = clean.Length > MaxSheetNameLength ? clean[..MaxSheetNameLength] : clean;
        for (var n = 2; !taken.Add(candidate); n++)
        {
            var suffix = $" ({n})";
            candidate = (clean.Length + suffix.Length > MaxSheetNameLength ? clean[..(MaxSheetNameLength - suffix.Length)] : clean) + suffix;
        }

        return candidate;
    }

    private static void Write(IXLCell cell, object? value)
    {
        switch (value)
        {
            case null:
                break;
            case int number:
                cell.Value = number;
                cell.Style.NumberFormat.Format = WholeNumberFormat;
                break;
            case bool flag:
                cell.Value = flag;
                break;
            case DateOnly date:
                cell.Value = date.ToDateTime(TimeOnly.MinValue);
                cell.Style.NumberFormat.Format = DateFormat;
                break;
            case DateTimeOffset instant:
                cell.Value = FederationCalendar.InMadrid(instant).DateTime;
                cell.Style.NumberFormat.Format = DateTimeFormat;
                break;
            case string text:
                cell.Value = XlsxExportWriter.Neutralised(text);
                break;
            default:
                throw new InvalidOperationException($"A workbook cell has an unsupported type ({value.GetType().Name}).");
        }
    }
}
