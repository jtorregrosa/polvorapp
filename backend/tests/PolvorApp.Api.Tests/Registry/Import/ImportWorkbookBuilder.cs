using System.Globalization;
using ClosedXML.Excel;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.ArquebusierRegistry.Import;

namespace PolvorApp.Api.Tests.Registry.Import;

/// <summary>
/// Builds import workbooks in memory with synthetic people only (SEC-11): no test reads a workbook
/// from disk. Headers default to the template's in one language; rows are lists of cell values
/// (<see cref="string"/>, <see cref="double"/>, <see cref="bool"/>, <see cref="DateTime"/>, <see cref="XLError"/>,
/// <see cref="Formula"/>, or null for a blank cell), written in header order.
/// </summary>
internal sealed class ImportWorkbookBuilder
{
    private readonly List<string?> _headers;
    private readonly List<(int RowNumber, object?[] Cells)> _rows = [];
    private int _nextRow = 2;
    private bool _formulaValues = true;
    private object?[]? _secondSheetRow;

    private ImportWorkbookBuilder(IEnumerable<string?> headers) => _headers = [.. headers];

    /// <summary>The template's 13 headers in <paramref name="culture"/>, in template order.</summary>
    public static ImportWorkbookBuilder Template(string culture = "es-ES")
    {
        var texts = ImportTemplateTexts.For(CultureInfo.GetCultureInfo(culture));
        return new ImportWorkbookBuilder(ImportColumns.All.Select(column => texts.Headers[column]));
    }

    public static ImportWorkbookBuilder WithHeaders(params string?[] headers) => new(headers);

    public IReadOnlyList<string?> Headers => _headers;

    /// <summary>Adds a data row on the next sheet row.</summary>
    public ImportWorkbookBuilder Row(params object?[] cells)
    {
        _rows.Add((_nextRow++, cells));
        return this;
    }

    /// <summary>Leaves the next sheet row blank.</summary>
    public ImportWorkbookBuilder BlankRow()
    {
        _nextRow++;
        return this;
    }

    /// <summary>
    /// A valid row of the 13-column template: a fresh synthetic identity, an AE license issued on
    /// 2024-03-10 and the course done, so it has no warning but the photo ones.
    /// </summary>
    public ImportWorkbookBuilder ValidRow(string lastName = "Sintético Importado", string firstName = "Arcabucero")
    {
        var (nationalId, federationId) = RegistryData.NextIdentity();
        return Row(
            (double)federationId, lastName, firstName, nationalId, new DateTime(1990, 5, 1), "Mujer",
            null, null, null, "AE", new DateTime(2024, 3, 10), null, new DateTime(2023, 11, 4));
    }

    /// <summary>Saves formulas without their values, as some tools other than Excel do.</summary>
    public ImportWorkbookBuilder WithoutFormulaValues()
    {
        _formulaValues = false;
        return this;
    }

    /// <summary>Adds a second sheet with the same headers and one data row.</summary>
    public ImportWorkbookBuilder WithSecondSheet(params object?[] cells)
    {
        _secondSheetRow = cells;
        return this;
    }

    /// <summary>The workbook as uploaded bytes.</summary>
    public byte[] Build()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Arcabuceros");
        WriteHeaders(sheet);
        foreach (var (rowNumber, cells) in _rows)
        {
            WriteRow(sheet, rowNumber, cells);
        }

        if (_secondSheetRow is not null)
        {
            var second = workbook.Worksheets.Add("Otra");
            WriteHeaders(second);
            WriteRow(second, 2, _secondSheetRow);
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream, new SaveOptions { EvaluateFormulasBeforeSaving = _formulaValues });
        return stream.ToArray();
    }

    private void WriteHeaders(IXLWorksheet sheet)
    {
        for (var column = 0; column < _headers.Count; column++)
        {
            if (_headers[column] is { } header)
            {
                sheet.Cell(1, column + 1).Value = header;
            }
        }
    }

    private static void WriteRow(IXLWorksheet sheet, int rowNumber, object?[] cells)
    {
        for (var column = 0; column < cells.Length; column++)
        {
            Write(sheet.Cell(rowNumber, column + 1), cells[column]);
        }
    }

    private static void Write(IXLCell cell, object? value)
    {
        switch (value)
        {
            case null:
                break;
            case Formula formula:
                cell.FormulaA1 = formula.Expression;
                break;
            case string text:
                // Text stays text, even when it looks like a number or a date.
                cell.SetValue(text);
                break;
            case DateTime date:
                cell.Value = date;
                break;
            case double number:
                cell.Value = number;
                break;
            case bool logical:
                cell.Value = logical;
                break;
            case XLError error:
                cell.Value = error;
                break;
            default:
                throw new ArgumentException($"Unsupported cell value {value.GetType().Name}.", nameof(value));
        }
    }

    /// <summary>A formula cell, saved with the value ClosedXML computes for it.</summary>
    public sealed record Formula(string Expression);
}
