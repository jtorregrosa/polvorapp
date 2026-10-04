namespace PolvorApp.Exports.Contracts;

/// <summary>
/// Renders documents with the exports' writers and fonts (ADR-0008; add-distribution-planning, design
/// D3), so every module's documents look alike and only one module sets up the PDF library. It holds
/// no data and applies no permission: the caller builds the content, checks who may have it and audits
/// the download before returning it.
/// </summary>
public interface IDocumentRenderer
{
    /// <summary>A table as an Excel workbook or a PDF, dated with the Federation's date of today.</summary>
    RenderedDocument RenderTable(DocumentTable table, DocumentFileFormat format);

    /// <summary>A one-page form to print and sign, as a PDF dated with the Federation's date of today.</summary>
    RenderedDocument RenderForm(DocumentForm form);

    /// <summary>A workbook of several sheets, as an Excel file (add-audit-privacy).</summary>
    RenderedDocument RenderWorkbook(DocumentWorkbook workbook);

    /// <summary>Arquebusier badges on A4 sheets to cut by hand, as a PDF (add-badges).</summary>
    RenderedDocument RenderBadgeSheet(DocumentBadgeSheet sheet);
}

/// <summary>A rendered file, ready to return: never stored (SEC-06).</summary>
/// <param name="Content">The file's bytes.</param>
/// <param name="ContentType">Its media type.</param>
/// <param name="FileName">The file name: the document's stem and the format's extension.</param>
public sealed record RenderedDocument(ReadOnlyMemory<byte> Content, string ContentType, string FileName)
{
    /// <summary>The type name only: the content is personal data.</summary>
    public override string ToString() => nameof(RenderedDocument);
}
