using PolvorApp.ComplianceInsights.Contracts;

namespace PolvorApp.ArquebusierRegistry.Import;

/// <summary>
/// The validation report of an import file (spec: Import validation report). Only rows with an
/// error or a warning are listed; the names are the Admin's own upload, returned uncached.
/// </summary>
/// <param name="RowCount">Non-blank data rows read.</param>
/// <param name="ValidCount">Rows without errors; they may have warnings.</param>
/// <param name="ErrorRowCount">Rows with at least one error; the file can be imported only when this is 0.</param>
/// <param name="WarningRowCount">Rows without errors and with at least one compliance warning (BR-04).</param>
/// <param name="IgnoredColumns">Header texts that are not template columns, as written.</param>
/// <param name="Rows">The rows with an error or a warning, in sheet order.</param>
internal sealed record ArquebusierImportReport(
    int RowCount,
    int ValidCount,
    int ErrorRowCount,
    int WarningRowCount,
    IReadOnlyList<string> IgnoredColumns,
    IReadOnlyList<ArquebusierImportRow> Rows)
{
    /// <summary>The counts only: the rows hold names.</summary>
    public override string ToString() => $"{nameof(ArquebusierImportReport)} {RowCount} rows, {ErrorRowCount} with errors";
}

/// <summary>A reported row.</summary>
/// <param name="RowNumber">The row's number in the sheet (the header is row 1).</param>
/// <param name="LastName">The last name as written, or null.</param>
/// <param name="FirstName">The first name as written, or null.</param>
/// <param name="Errors">Blocking problems, in column order; none when the row only has warnings.</param>
/// <param name="Warnings">Compliance warnings of a valid row, in rule order, without the photo warnings.</param>
internal sealed record ArquebusierImportRow(
    int RowNumber,
    string? LastName,
    string? FirstName,
    IReadOnlyList<ArquebusierImportError> Errors,
    IReadOnlyList<ComplianceWarning> Warnings)
{
    public override string ToString() => $"{nameof(ArquebusierImportRow)} {RowNumber}";
}

/// <summary>A blocking problem of a row, named like a registration error so the UI translates both alike.</summary>
/// <param name="Field">The field, e.g. <c>nationalId</c> or <c>license.issuedOn</c>.</param>
/// <param name="Reason">The reason, e.g. <c>checkLetter</c>, <c>duplicateInFile</c> or <c>taken</c>.</param>
internal sealed record ArquebusierImportError(string Field, string Reason);
