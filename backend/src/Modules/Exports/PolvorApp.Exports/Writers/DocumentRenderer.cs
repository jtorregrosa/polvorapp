using PolvorApp.Exports.Contracts;
using PolvorApp.SharedKernel.Time;

namespace PolvorApp.Exports.Writers;

/// <summary>
/// <see cref="IDocumentRenderer"/> on the exports' writers (add-distribution-planning, design D3), also
/// used by the exports themselves. The generation date printed in a PDF is the Federation's date of
/// today (Europe/Madrid).
/// </summary>
internal sealed class DocumentRenderer(TimeProvider time) : IDocumentRenderer
{
    public RenderedDocument RenderTable(DocumentTable table, DocumentFileFormat format)
    {
        ArgumentNullException.ThrowIfNull(table);
        return format switch
        {
            DocumentFileFormat.Xlsx => new RenderedDocument(
                XlsxExportWriter.Write(table), XlsxExportWriter.ContentType, $"{table.FileStem}.{XlsxExportWriter.Extension}"),
            DocumentFileFormat.Pdf => new RenderedDocument(
                PdfExportWriter.Write(table, FederationCalendar.Today(time)), PdfExportWriter.ContentType, $"{table.FileStem}.{PdfExportWriter.Extension}"),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown document format."),
        };
    }

    public RenderedDocument RenderForm(DocumentForm form)
    {
        ArgumentNullException.ThrowIfNull(form);
        return new RenderedDocument(
            PdfFormWriter.Write(form, FederationCalendar.Today(time)), PdfExportWriter.ContentType, $"{form.FileStem}.{PdfExportWriter.Extension}");
    }
}
