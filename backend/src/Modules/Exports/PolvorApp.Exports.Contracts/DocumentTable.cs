using System.Text.Json.Serialization;
using PolvorApp.SharedKernel.Codes;

namespace PolvorApp.Exports.Contracts;

/// <summary>A file format of a document (add-exports spec: Excel and PDF).</summary>
[JsonConverter(typeof(CodeEnumConverter<DocumentFileFormat>))]
public enum DocumentFileFormat
{
    /// <summary>An Excel workbook.</summary>
    [JsonStringEnumMemberName("XLSX")]
    Xlsx,

    /// <summary>A PDF document.</summary>
    [JsonStringEnumMemberName("PDF")]
    Pdf,
}

/// <summary>How a cell's value is written: the writers format it, the builders only type it.</summary>
public enum DocumentCellType
{
    /// <summary>Text, written as a string, never a formula.</summary>
    Text,

    /// <summary>A whole number.</summary>
    Number,

    /// <summary>A date, written <c>dd/mm/yyyy</c>.</summary>
    Date,
}

/// <summary>A column: its translated heading and the type of its cells.</summary>
/// <param name="Header">The translated heading.</param>
/// <param name="Type">The type of its cells.</param>
/// <param name="ForHandwriting">
/// A column left empty to be filled in by hand on paper (e.g. a flask number on distribution day): the
/// PDF gives it a fixed width and its rows a writable height; Excel writes it as any column.
/// </param>
public sealed record DocumentColumn(string Header, DocumentCellType Type, bool ForHandwriting = false);

/// <summary>
/// One document as a table (add-exports design D3; add-distribution-planning design D3): what both
/// writers render. Cell values are <see cref="string"/>, <see cref="int"/>, <see cref="DateOnly"/> or
/// null, as their column's type says; the total row's first cell may be text whatever its column (its
/// label). A column for handwriting is text and stays empty. The constructor checks all of it and keeps
/// its own copy of the rows, so the table cannot change after it was checked.
/// </summary>
public sealed class DocumentTable
{
    public DocumentTable(
        string fileStem,
        string title,
        IReadOnlyList<string> notices,
        string versionLine,
        IReadOnlyList<DocumentColumn> columns,
        IReadOnlyList<IReadOnlyList<object?>> rows,
        IReadOnlyList<object?>? totalRow)
    {
        DocumentFileStem.Check(fileStem, nameof(fileStem));
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(notices);
        ArgumentNullException.ThrowIfNull(versionLine);
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(rows);
        if (columns.Count == 0)
        {
            throw new ArgumentException("A document table needs at least one column.", nameof(columns));
        }

        foreach (var column in columns)
        {
            CheckColumn(column);
        }

        foreach (var row in rows)
        {
            ArgumentNullException.ThrowIfNull(row, nameof(rows));
            CheckRow(columns, row, isTotal: false);
        }

        if (totalRow is not null)
        {
            CheckRow(columns, totalRow, isTotal: true);
        }

        FileStem = fileStem;
        Title = title;
        Notices = [.. notices.Select(notice => notice ?? throw new ArgumentNullException(nameof(notices)))];
        VersionLine = versionLine;
        Columns = [.. columns];
        Rows = [.. rows.Select(row => (IReadOnlyList<object?>)[.. row])];
        TotalRow = totalRow is null ? null : [.. totalRow];
    }

    /// <summary>The file name without extension, e.g. <c>polvorapp-2031-arms-authority-provisional</c>.</summary>
    public string FileStem { get; }

    /// <summary>The document title.</summary>
    public string Title { get; }

    /// <summary>The lines before the table, e.g. a draft notice, then the provisional notice.</summary>
    public IReadOnlyList<string> Notices { get; }

    /// <summary>The definition's name and version, as written in the document.</summary>
    public string VersionLine { get; }

    public IReadOnlyList<DocumentColumn> Columns { get; }

    /// <summary>The rows, already sorted.</summary>
    public IReadOnlyList<IReadOnlyList<object?>> Rows { get; }

    /// <summary>The total row, when the document has one.</summary>
    public IReadOnlyList<object?>? TotalRow { get; }

    /// <summary>The type name only: the rows are personal data.</summary>
    public override string ToString() => nameof(DocumentTable);

    private static void CheckColumn(DocumentColumn column)
    {
        ArgumentNullException.ThrowIfNull(column);
        ArgumentNullException.ThrowIfNull(column.Header);
        if (!Enum.IsDefined(column.Type))
        {
            throw new ArgumentException($"The document column '{column.Header}' has an unknown type.", nameof(column));
        }

        if (column.ForHandwriting && column.Type != DocumentCellType.Text)
        {
            throw new ArgumentException($"The handwriting column '{column.Header}' must be a text column.", nameof(column));
        }
    }

    /// <summary>One cell per column, each null or of its column's type; errors name the column, never the value.</summary>
    private static void CheckRow(IReadOnlyList<DocumentColumn> columns, IReadOnlyList<object?> row, bool isTotal)
    {
        if (row.Count != columns.Count)
        {
            throw new InvalidOperationException($"A document row has {row.Count} cells for {columns.Count} columns.");
        }

        for (var i = 0; i < row.Count; i++)
        {
            var column = columns[i];
            var fits = row[i] switch
            {
                null => true,
                _ when column.ForHandwriting => false,
                string => column.Type == DocumentCellType.Text || (isTotal && i == 0),
                int => column.Type == DocumentCellType.Number,
                DateOnly => column.Type == DocumentCellType.Date,
                _ => false,
            };
            if (!fits)
            {
                throw new InvalidOperationException(column.ForHandwriting
                    ? $"The handwriting column '{column.Header}' must stay empty."
                    : $"A cell of the document column '{column.Header}' is not of the type {column.Type}.");
            }
        }
    }
}
