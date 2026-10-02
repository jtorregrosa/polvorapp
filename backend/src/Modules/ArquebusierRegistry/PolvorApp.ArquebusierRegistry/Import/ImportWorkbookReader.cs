using System.Collections.Frozen;
using System.Globalization;
using ClosedXML.Excel;
using Microsoft.Extensions.Logging;

namespace PolvorApp.ArquebusierRegistry.Import;

/// <summary>
/// Reads the first sheet of an uploaded workbook (spec: Import file reading; design D3): the package
/// is checked first, then the header row is matched against the template headers of every language,
/// and the non-blank data rows are returned with the saved value of each recognised cell. Formulas
/// are never recalculated; a formula saved without its value reads as an error cell. Cell values
/// are personal data and are never logged.
/// </summary>
internal static partial class ImportWorkbookReader
{
    public const int MaxRows = 1000;

    /// <summary>Largest upload the import reads (spec: Import file reading).</summary>
    public const long MaxFileBytes = 2 * 1024 * 1024;

    /// <summary>Header cells beyond this column are not looked at; the template has 13.</summary>
    private const int MaxColumns = 100;

    /// <summary>Longest header text compared or kept for the report.</summary>
    private const int MaxHeaderLength = 100;

    private static readonly FrozenDictionary<string, ImportColumn> ColumnByHeader = ImportTemplateTexts.All
        .SelectMany(texts => texts.Headers)
        .Select(header => (Key: ImportText.Normalise(header.Value), Column: header.Key))
        .DistinctBy(header => header.Key)
        .ToFrozenDictionary(header => header.Key, header => header.Column, StringComparer.Ordinal);

    public static (ImportSheet? Sheet, ImportFileProblem? Problem) Read(byte[] content, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (WorkbookPackage.Rejection(content) is { } rejection)
        {
            LogRejectedPackage(logger, rejection);
            return (null, ImportFileProblem.Of(ImportFileProblem.Invalid));
        }

        using var workbook = Load(content, logger);
        return workbook is null
            ? (null, ImportFileProblem.Of(ImportFileProblem.Invalid))
            : ReadSheet(workbook.Worksheet(1));
    }

    /// <summary>The workbook, or null when ClosedXML cannot load it: any such failure means the file is not a readable workbook.</summary>
    private static XLWorkbook? Load(byte[] content, ILogger logger)
    {
        try
        {
            var workbook = new XLWorkbook(new MemoryStream(content, writable: false));
            if (workbook.Worksheets.Count > 0)
            {
                return workbook;
            }

            workbook.Dispose();
            LogUnreadableWorkbook(logger, "NoWorksheet", string.Empty);
            return null;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Types only: exception messages can quote the file's content.
            var failure = exception.GetType().Name;
            var cause = exception.GetBaseException().GetType().Name;
            LogUnreadableWorkbook(logger, failure, cause);
            return null;
        }
    }

    private static (ImportSheet? Sheet, ImportFileProblem? Problem) ReadSheet(IXLWorksheet sheet)
    {
        var (columns, ignored, headerProblem) = ReadHeader(sheet.Row(1));
        if (headerProblem is not null)
        {
            return (null, headerProblem);
        }

        List<ImportRow> rows = [];
        foreach (var sheetRow in sheet.RowsUsed(XLCellsUsedOptions.Contents))
        {
            var rowNumber = sheetRow.RowNumber();
            if (rowNumber == 1)
            {
                continue;
            }

            var cells = columns.ToDictionary(column => column.Key, column => SavedValue(sheetRow.Cell(column.Value)));
            if (cells.Values.All(ImportRow.IsBlank))
            {
                continue;
            }

            if (rows.Count == MaxRows)
            {
                return (null, ImportFileProblem.Of(ImportFileProblem.TooManyRows));
            }

            rows.Add(new ImportRow(rowNumber, cells));
        }

        return rows.Count == 0
            ? (null, ImportFileProblem.Of(ImportFileProblem.Empty))
            : (new ImportSheet(ignored, rows), null);
    }

    /// <summary>The value saved in the file; a formula saved without one is an error, never an empty cell.</summary>
    private static XLCellValue SavedValue(IXLCell cell) =>
        cell.HasFormula && cell.CachedValue.IsBlank ? XLError.NoValueAvailable : cell.CachedValue;

    private static (Dictionary<ImportColumn, int> Columns, List<string> Ignored, ImportFileProblem? Problem) ReadHeader(IXLRow header)
    {
        Dictionary<ImportColumn, int> columns = [];
        List<string> ignored = [];
        SortedSet<ImportColumn> repeated = [];
        var lastColumn = Math.Min(header.LastCellUsed(XLCellsUsedOptions.Contents)?.Address.ColumnNumber ?? 0, MaxColumns);
        for (var columnNumber = 1; columnNumber <= lastColumn; columnNumber++)
        {
            var text = HeaderText(header.Cell(columnNumber).CachedValue);
            if (text.Length == 0)
            {
                continue;
            }

            if (!ColumnByHeader.TryGetValue(ImportText.Normalise(text), out var column))
            {
                ignored.Add(text);
            }
            else if (!columns.TryAdd(column, columnNumber))
            {
                repeated.Add(column);
            }
        }

        if (repeated.Count > 0)
        {
            return (columns, ignored, new ImportFileProblem(ImportFileProblem.DuplicateColumns, [.. repeated.Select(ImportColumns.Field)]));
        }

        var missing = ImportColumns.All.Where(column => ImportColumns.IsRequired(column) && !columns.ContainsKey(column)).ToList();
        return missing.Count > 0
            ? (columns, ignored, new ImportFileProblem(ImportFileProblem.MissingColumns, [.. missing.Select(ImportColumns.Field)]))
            : (columns, ignored, null);
    }

    /// <summary>The header as written, trimmed and cut to <see cref="MaxHeaderLength"/>; any value kind is listed if unknown.</summary>
    private static string HeaderText(XLCellValue value)
    {
        var text = (value.Type switch
        {
            XLDataType.Blank => string.Empty,
            XLDataType.Text => value.GetText(),
            XLDataType.Number => value.GetNumber().ToString(CultureInfo.InvariantCulture),
            _ => value.ToString(CultureInfo.InvariantCulture),
        }).Trim();
        return text.Length > MaxHeaderLength ? text[..MaxHeaderLength] : text;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Import package refused ({Rejection})")]
    private static partial void LogRejectedPackage(ILogger logger, string rejection);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Import workbook could not be loaded ({Failure}, caused by {Cause})")]
    private static partial void LogUnreadableWorkbook(ILogger logger, string failure, string cause);
}
