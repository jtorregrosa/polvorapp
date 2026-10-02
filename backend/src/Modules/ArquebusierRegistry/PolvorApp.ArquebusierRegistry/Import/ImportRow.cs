using ClosedXML.Excel;

namespace PolvorApp.ArquebusierRegistry.Import;

/// <summary>
/// One non-blank data row: its number in the sheet and the saved value of each recognised column
/// present in the file. The values are personal data: never log them.
/// </summary>
internal sealed record ImportRow(int RowNumber, IReadOnlyDictionary<ImportColumn, XLCellValue> Cells)
{
    public override string ToString() => $"{nameof(ImportRow)} {RowNumber}";

    /// <summary>Empty, or text made of spaces only.</summary>
    public static bool IsBlank(XLCellValue value) => value.IsBlank || (value.IsText && string.IsNullOrWhiteSpace(value.GetText()));
}
