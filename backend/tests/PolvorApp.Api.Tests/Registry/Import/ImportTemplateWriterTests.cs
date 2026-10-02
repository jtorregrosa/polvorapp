using System.Globalization;
using ClosedXML.Excel;
using Microsoft.Extensions.Logging.Abstractions;
using PolvorApp.ArquebusierRegistry.Import;

namespace PolvorApp.Api.Tests.Registry.Import;

/// <summary>
/// The import template (spec: Import template; design D8): the 13 headers in the user's language,
/// text and date formats, drop-down lists, an instructions sheet, and no data.
/// </summary>
public sealed class ImportTemplateWriterTests
{
    public static TheoryData<string> Cultures => ["es-ES", "ca-ES-valencia", "en"];

    [Theory]
    [MemberData(nameof(Cultures))]
    public void The_data_sheet_has_the_headers_in_template_order(string culture)
    {
        var texts = Texts(culture);
        using var workbook = Open(texts);
        var sheet = workbook.Worksheet(1);

        Assert.Equal(texts.DataSheet, sheet.Name);
        Assert.Equal(
            ImportColumns.All.Select(column => texts.Headers[column]),
            ImportColumns.All.Select((_, index) => sheet.Cell(1, index + 1).GetText()));
        Assert.True(sheet.Cell(1, 1).Style.Font.Bold);
        Assert.Equal(1, sheet.SheetView.SplitRow);
    }

    [Fact]
    public void Identity_and_phone_columns_are_text_and_date_columns_are_dates()
    {
        using var workbook = Open(Texts("es-ES"));
        var sheet = workbook.Worksheet(1);

        foreach (var column in ImportColumns.All)
        {
            var format = sheet.Cell(2, Number(column)).Style.NumberFormat.Format;
            if (column is ImportColumn.NationalId or ImportColumn.Phone)
            {
                Assert.Equal("@", format);
            }
            else if (ImportColumns.IsDate(column))
            {
                Assert.Equal("dd/mm/yyyy", format);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void Gender_status_and_license_type_offer_their_values(string culture)
    {
        var texts = Texts(culture);
        using var workbook = Open(texts);
        var sheet = workbook.Worksheet(1);

        Assert.Equal(texts.Genders.Values.Order(), Options(sheet, ImportColumn.Gender).Order());
        Assert.Equal(texts.Statuses.Values.Order(), Options(sheet, ImportColumn.Status).Order());
        Assert.Equal(["A-PROF", "AE"], Options(sheet, ImportColumn.LicenseType).Order());
        Assert.All(new[] { ImportColumn.Gender, ImportColumn.Status, ImportColumn.LicenseType }, column =>
        {
            var validation = Validation(sheet, column);
            Assert.Contains(validation.Ranges, range =>
                range.RangeAddress.FirstAddress.RowNumber == 2 && range.RangeAddress.LastAddress.RowNumber == 1 + ImportWorkbookReader.MaxRows);
        });
        Assert.Equal(XLWorksheetVisibility.Hidden, workbook.Worksheet(texts.ListsSheet).Visibility);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void The_instructions_explain_every_column_and_the_limits(string culture)
    {
        var texts = Texts(culture);
        using var workbook = Open(texts);
        var instructions = workbook.Worksheet(texts.InstructionsSheet);

        Assert.Equal(2, instructions.Position);
        var text = string.Join("\n", instructions.CellsUsed().Select(cell => cell.GetText()));
        Assert.All(ImportColumns.All, column =>
        {
            Assert.Contains(texts.Headers[column], text, StringComparison.Ordinal);
            Assert.Contains(texts.Descriptions[column], text, StringComparison.Ordinal);
        });
        Assert.All(texts.Notes, note => Assert.Contains(note, text, StringComparison.Ordinal));
        Assert.Contains(texts.Yes, text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_template_holds_no_data_and_no_formulas()
    {
        using var workbook = Open(Texts("es-ES"));

        Assert.Equal(1, workbook.Worksheet(1).LastRowUsed(XLCellsUsedOptions.Contents)!.RowNumber());
        Assert.DoesNotContain(workbook.Worksheets.SelectMany(sheet => sheet.CellsUsed()), cell => cell.HasFormula);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void The_reader_recognises_the_template_and_finds_it_empty(string culture)
    {
        var content = ImportTemplateWriter.Write(Texts(culture));

        Assert.Null(WorkbookPackage.Rejection(content));
        Assert.Equal("empty", ImportWorkbookReader.Read(content, NullLogger.Instance).Problem!.Reason);
    }

    [Fact]
    public void A_filled_in_template_is_read()
    {
        using var workbook = Open(Texts("es-ES"));
        var sheet = workbook.Worksheet(1);
        sheet.Cell(2, Number(ImportColumn.FederationId)).Value = 900001;
        sheet.Cell(2, Number(ImportColumn.LastName)).Value = "Sintética";
        sheet.Cell(2, Number(ImportColumn.FirstName)).Value = "Arcabucera";
        sheet.Cell(2, Number(ImportColumn.NationalId)).Value = "01234567L";
        sheet.Cell(2, Number(ImportColumn.BirthDate)).Value = new DateTime(1990, 5, 1);
        sheet.Cell(2, Number(ImportColumn.Gender)).Value = "Mujer";
        sheet.Cell(2, Number(ImportColumn.Phone)).Value = "600123456";
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        var row = ImportWorkbookReader.Read(stream.ToArray(), NullLogger.Instance).Sheet!.Rows.Single();
        var (input, errors) = ImportCellReader.Read(row, new DateOnly(2026, 10, 2));

        Assert.Empty(errors);
        Assert.Equal("01234567L", input!.NationalId);
        Assert.Equal(new DateOnly(1990, 5, 1), input.BirthDate);
    }

    private static ImportTemplateTexts Texts(string culture) => ImportTemplateTexts.For(CultureInfo.GetCultureInfo(culture));

    private static XLWorkbook Open(ImportTemplateTexts texts) => new(new MemoryStream(ImportTemplateWriter.Write(texts)));

    private static int Number(ImportColumn column) => (int)column + 1;

    private static IXLDataValidation Validation(IXLWorksheet sheet, ImportColumn column) =>
        sheet.DataValidations.Single(validation => validation.Ranges.Any(range => range.RangeAddress.FirstAddress.ColumnNumber == Number(column)));

    /// <summary>The values of the list the column's validation points to.</summary>
    private static IEnumerable<string> Options(IXLWorksheet sheet, ImportColumn column)
    {
        var validation = Validation(sheet, column);
        Assert.Equal(XLAllowedValues.List, validation.AllowedValues);
        var source = sheet.Workbook.Range(validation.Value.TrimStart('='));
        return source.CellsUsed().Select(cell => cell.GetText());
    }
}
