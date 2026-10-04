namespace PolvorApp.Exports.Contracts;

/// <summary>
/// A workbook of several sheets, e.g. a person's data export (add-audit-privacy, design D11). Cells may
/// be text, whole numbers, true/false, dates or instants (shown in Europe/Madrid). Text is neutralised
/// against formula injection like every export.
/// </summary>
public sealed record DocumentWorkbook(string FileStem, IReadOnlyList<DocumentSheet> Sheets)
{
    /// <summary>The type name only: the sheets are personal data.</summary>
    public override string ToString() => nameof(DocumentWorkbook);
}

/// <summary>One sheet: its name (at most 31 characters, as Excel allows), headers and rows.</summary>
public sealed record DocumentSheet(string Name, IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<object?>> Rows)
{
    /// <summary>The name and size only: the rows are personal data.</summary>
    public override string ToString() => $"DocumentSheet({Name}, {Rows.Count} rows)";
}
