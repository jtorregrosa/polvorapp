using System.Text.Json.Serialization;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.SharedKernel.Codes;

namespace PolvorApp.Exports.Definitions;

/// <summary>A file format of an export (spec: Excel and PDF).</summary>
[JsonConverter(typeof(CodeEnumConverter<ExportFormat>))]
internal enum ExportFormat
{
    /// <summary>An Excel workbook.</summary>
    [JsonStringEnumMemberName("XLSX")]
    Xlsx,

    /// <summary>A PDF document.</summary>
    [JsonStringEnumMemberName("PDF")]
    Pdf,
}

/// <summary>Who an export is for, which decides who may download it (spec: Who may export (BR-12)).</summary>
[JsonConverter(typeof(CodeEnumConverter<ExportAudience>))]
internal enum ExportAudience
{
    /// <summary>A recipient of the Federation (supplier, rental company, Arms Authority): Admins only, validated orders only.</summary>
    [JsonStringEnumMemberName("RECIPIENT")]
    Recipient,

    /// <summary>One comparsa's list: Admins and the comparsa's FiringChiefs, an order in any status.</summary>
    [JsonStringEnumMemberName("COMPARSA")]
    Comparsa,
}

/// <summary>How a cell's value is written: the writers format it, the definitions only type it.</summary>
internal enum ExportCellType
{
    Text,
    Integer,
    Date,
}

/// <summary>A column: its translated heading and the type of its cells.</summary>
internal sealed record ExportColumn(string Header, ExportCellType Type);

/// <summary>
/// One export as a table (design D3): what both writers render. Cell values are <see cref="string"/>,
/// <see cref="int"/>, <see cref="DateOnly"/> or null, as their column's type says; the first cell of a
/// row may be text whatever its column (the total row's label). The constructor checks it.
/// </summary>
internal sealed class ExportTable
{
    public ExportTable(
        string fileStem,
        string title,
        IReadOnlyList<string> notices,
        string versionLine,
        IReadOnlyList<ExportColumn> columns,
        IReadOnlyList<IReadOnlyList<object?>> rows,
        IReadOnlyList<object?>? totalRow)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(rows);
        foreach (var row in totalRow is null ? rows : rows.Append(totalRow))
        {
            Check(columns, row);
        }

        (FileStem, Title, Notices, VersionLine, Columns, Rows, TotalRow) = (fileStem, title, notices, versionLine, columns, rows, totalRow);
    }

    /// <summary>The file name without extension, e.g. <c>polvorapp-2031-arms-authority-provisional</c>.</summary>

    public string FileStem { get; }

    /// <summary>The document title.</summary>
    public string Title { get; }

    /// <summary>The lines before the table: a draft notice, then the provisional notice.</summary>
    public IReadOnlyList<string> Notices { get; }

    /// <summary>The definition's name and version, as written in the document.</summary>
    public string VersionLine { get; }

    public IReadOnlyList<ExportColumn> Columns { get; }

    /// <summary>The rows, already sorted.</summary>
    public IReadOnlyList<IReadOnlyList<object?>> Rows { get; }

    /// <summary>The total row, when the definition has one.</summary>
    public IReadOnlyList<object?>? TotalRow { get; }

    /// <summary>The type name only: the rows are personal data.</summary>
    public override string ToString() => nameof(ExportTable);

    /// <summary>One cell per column, each null or of its column's type; errors name the column, never the value.</summary>
    private static void Check(IReadOnlyList<ExportColumn> columns, IReadOnlyList<object?> row)
    {
        if (row.Count != columns.Count)
        {
            throw new InvalidOperationException($"An export row has {row.Count} cells for {columns.Count} columns.");
        }

        for (var i = 0; i < row.Count; i++)
        {
            var fits = row[i] switch
            {
                null => true,
                string => columns[i].Type == ExportCellType.Text || i == 0,
                int => columns[i].Type == ExportCellType.Integer,
                DateOnly => columns[i].Type == ExportCellType.Date,
                _ => false,
            };
            if (!fits)
            {
                throw new InvalidOperationException($"A cell of the export column '{columns[i].Header}' is not of the type {columns[i].Type}.");
            }
        }
    }
}

/// <summary>
/// What a definition is built from, read once per request (design D3): the orders, the names of
/// their comparsas and weapon models, and the arquebusiers still in the registry, by id.
/// </summary>
internal sealed record ExportData(
    int EditionYear,
    IReadOnlyList<ExportedOrder> Orders,
    IReadOnlyDictionary<Guid, string> ComparsaNames,
    IReadOnlyDictionary<Guid, string> ModelLabels,
    IReadOnlyDictionary<Guid, RosterArquebusier> Arquebusiers)
{
    /// <summary>The type name only.</summary>
    public override string ToString() => nameof(ExportData);
}

/// <summary>A named, versioned export definition (ADR-0008; spec: Export definitions).</summary>
/// <remarks>The table's notices come first: a draft notice, then the provisional notice.</remarks>
internal interface IExportDefinition
{
    /// <summary>The name in routes, files and the audit trail, e.g. <c>arms-authority</c>.</summary>
    string Name { get; }

    /// <summary>The version written in the document; <see cref="ExportRows.ProvisionalVersion"/> until the real template.</summary>
    string Version { get; }

    /// <summary>True until the recipient's template arrives (Q-44).</summary>
    bool Provisional { get; }

    ExportAudience Audience { get; }

    /// <summary>The table, in the texts' language, from data already scoped and filtered by the caller.</summary>
    ExportTable Build(ExportData data, ExportTexts texts);
}
