using ClosedXML.Excel;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.SharedKernel.Codes;

namespace PolvorApp.ArquebusierRegistry.Import;

/// <summary>
/// Writes the import template in one language (spec: Import template; design D8): the data sheet
/// with the 13 headers, text and date formats and drop-down lists, an instructions sheet, and a
/// hidden sheet holding the lists. It holds no data and no formulas.
/// </summary>
internal static class ImportTemplateWriter
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public const string FileName = "polvorapp-arquebusiers-template.xlsx";

    private const string DateFormat = "dd/mm/yyyy";
    private const string TextFormat = "@";
    private const int FirstDataRow = 2;
    private const int LastDataRow = ImportWorkbookReader.MaxRows + 1;

    public static byte[] Write(ImportTemplateTexts texts)
    {
        ArgumentNullException.ThrowIfNull(texts);
        using var workbook = new XLWorkbook();
        var data = workbook.Worksheets.Add(texts.DataSheet);
        var instructions = workbook.Worksheets.Add(texts.InstructionsSheet);
        var lists = workbook.Worksheets.Add(texts.ListsSheet);

        WriteData(data, texts, WriteLists(lists, texts));
        WriteInstructions(instructions, texts);
        lists.Hide();
        data.SetTabActive();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void WriteData(IXLWorksheet sheet, ImportTemplateTexts texts, Dictionary<ImportColumn, IXLRange> lists)
    {
        foreach (var column in ImportColumns.All)
        {
            var number = Number(column);
            var header = sheet.Cell(1, number);
            header.Value = texts.Headers[column];
            header.Style.Font.Bold = true;

            // Whole-column formats live in the column definition, so no empty rows are written.
            var format = column is ImportColumn.NationalId or ImportColumn.Phone ? TextFormat
                : ImportColumns.IsDate(column) ? DateFormat
                : null;
            if (format is not null)
            {
                sheet.Column(number).Style.NumberFormat.Format = format;
                header.Style.NumberFormat.Format = TextFormat;
            }

            if (lists.TryGetValue(column, out var list))
            {
                sheet.Range(FirstDataRow, number, LastDataRow, number).CreateDataValidation().List(list, true);
            }

            sheet.Column(number).Width = Math.Max(14, texts.Headers[column].Length + 4);
        }

        sheet.SheetView.FreezeRows(1);
    }

    /// <summary>One column per list: the translated genders and statuses, and the license types as the Federation writes them.</summary>
    private static Dictionary<ImportColumn, IXLRange> WriteLists(IXLWorksheet sheet, ImportTemplateTexts texts)
    {
        return new Dictionary<ImportColumn, IXLRange>
        {
            [ImportColumn.Gender] = List(1, Enum.GetValues<Gender>().Select(gender => texts.Genders[gender])),
            [ImportColumn.Status] = List(2, Enum.GetValues<ArquebusierStatus>().Select(status => texts.Statuses[status])),
            [ImportColumn.LicenseType] = List(3, Enum.GetValues<LicenseType>().Select(type => EnumCodes.ToCode(type).Replace('_', '-'))),
        };

        IXLRange List(int column, IEnumerable<string> values)
        {
            var items = values.ToList();
            for (var row = 0; row < items.Count; row++)
            {
                sheet.Cell(row + 1, column).SetValue(items[row]);
            }

            return sheet.Range(1, column, items.Count, column);
        }
    }

    private static void WriteInstructions(IXLWorksheet sheet, ImportTemplateTexts texts)
    {
        var headings = new[] { texts.ColumnHeading, texts.RequiredHeading, texts.DescriptionHeading };
        for (var column = 0; column < headings.Length; column++)
        {
            sheet.Cell(1, column + 1).SetValue(headings[column]);
            sheet.Cell(1, column + 1).Style.Font.Bold = true;
        }

        var row = 2;
        foreach (var column in ImportColumns.All)
        {
            sheet.Cell(row, 1).SetValue(texts.Headers[column]);
            sheet.Cell(row, 2).SetValue(ImportColumns.IsRequired(column) ? texts.Yes : texts.No);
            sheet.Cell(row, 3).SetValue(texts.Descriptions[column]);
            row++;
        }

        row++;
        foreach (var note in texts.Notes)
        {
            sheet.Cell(row++, 1).SetValue(note);
        }

        sheet.Column(1).Width = 24;
        sheet.Column(2).Width = 12;
        sheet.Column(3).Width = 100;
        sheet.Column(3).Style.Alignment.WrapText = true;
    }

    private static int Number(ImportColumn column) => (int)column + 1;
}
