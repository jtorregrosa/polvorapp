namespace PolvorApp.ArquebusierRegistry.Import;

/// <summary>The data rows of an import sheet, with the headers that were not recognised.</summary>
internal sealed record ImportSheet(IReadOnlyList<string> IgnoredColumns, IReadOnlyList<ImportRow> Rows);
