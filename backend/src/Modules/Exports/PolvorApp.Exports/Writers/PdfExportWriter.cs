using System.Globalization;
using PolvorApp.Exports.Contracts;
using PolvorApp.Exports.Definitions;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PolvorApp.Exports.Writers;

/// <summary>
/// Writes a <see cref="DocumentTable"/> as a PDF (spec: Excel and PDF; design D4): A4, landscape above
/// six columns; the title, notices and version line, then the table whose header repeats on every
/// page; a footer with the generation date and "page / pages". Geist only, so the output is the same
/// on every host. Numbers are written without thousands separators (they are counts and IDs). A
/// failure surfaces as a <see cref="DocumentRenderingException"/> that never quotes the content.
/// </summary>
internal static class PdfExportWriter
{
    public const string ContentType = "application/pdf";

    public const string Extension = "pdf";

    /// <summary>The width of a column filled in by hand, in points: room for a number or a short code.</summary>
    public const float HandwritingColumnWidth = 72;

    /// <summary>The minimum row height of a table with columns filled in by hand, in points: room to write.</summary>
    public const float HandwritingRowHeight = 22;

    private const int PortraitMaxColumns = 6;
    private const float DateWidth = 62;
    private const float NumberWidth = 56;

    public static byte[] Write(DocumentTable table, DateOnly generatedOn)
    {
        ArgumentNullException.ThrowIfNull(table);
        PdfSetup.EnsureApplied();
        var landscape = table.Columns.Count > PortraitMaxColumns;
        return PdfSetup.Generate("table", PdfSetup.Metadata(table.Title, generatedOn), document => document.Page(page =>
        {
            page.Size(landscape ? PageSizes.A4.Landscape() : PageSizes.A4);
            page.Margin(28);
            page.DefaultTextStyle(style => style.FontFamily(PdfSetup.FontFamily).FontSize(landscape ? 8 : 9));
            page.Content().Column(column =>
            {
                column.Spacing(4);
                column.Item().Text(table.Title).Bold().FontSize(13);
                foreach (var notice in table.Notices)
                {
                    column.Item().Text(notice).Bold();
                }

                column.Item().Text(table.VersionLine).FontColor(Colors.Grey.Darken2);
                column.Item().PaddingTop(6).Element(container => Table(container, table));
            });
            page.Footer().AlignRight().Text(text =>
            {
                text.Span(generatedOn.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) + " · ");
                text.CurrentPageNumber();
                text.Span(" / ");
                text.TotalPages();
            });
        }));
    }

    private static void Table(IContainer container, DocumentTable table) => container.Table(grid =>
    {
        grid.ColumnsDefinition(columns =>
        {
            // Dates and numbers get fixed widths that fit "31/05/2033" and a six-digit ID unbroken;
            // text columns share the rest.
            foreach (var column in table.Columns)
            {
                if (column.ForHandwriting)
                {
                    columns.ConstantColumn(HandwritingColumnWidth);
                    continue;
                }

                switch (column.Type)
                {
                    case DocumentCellType.Date:
                        columns.ConstantColumn(DateWidth);
                        break;
                    case DocumentCellType.Number:
                        columns.ConstantColumn(NumberWidth);
                        break;
                    default:
                        columns.RelativeColumn();
                        break;
                }
            }
        });
        grid.Header(header =>
        {
            foreach (var column in table.Columns)
            {
                header.Cell().Background(Colors.Grey.Lighten3).BorderBottom(0.5f).Padding(3).Text(column.Header).Bold();
            }
        });
        // Rows are tall enough to write in only when the table asks for handwriting.
        var minHeight = table.Columns.Any(column => column.ForHandwriting) ? HandwritingRowHeight : 0;
        foreach (var row in table.Rows)
        {
            Row(grid, table.Columns, row, bold: false, minHeight);
        }

        if (table.TotalRow is { } total)
        {
            Row(grid, table.Columns, total, bold: true, minHeight);
        }
    });

    private static void Row(TableDescriptor grid, IReadOnlyList<DocumentColumn> columns, IReadOnlyList<object?> cells, bool bold, float minHeight)
    {
        for (var c = 0; c < cells.Count; c++)
        {
            var cell = grid.Cell().BorderBottom(0.25f).BorderColor(Colors.Grey.Lighten2);
            if (minHeight > 0)
            {
                cell = cell.MinHeight(minHeight);
            }

            cell = cell.Padding(3);
            if (cells[c] is int or DateOnly)
            {
                cell = cell.AlignRight();
            }

            var text = cell.Text(Format(columns[c], cells[c]));
            if (bold)
            {
                text.Bold();
            }
        }
    }

    private static string Format(DocumentColumn column, object? value) => value switch
    {
        null => string.Empty,
        int number => number.ToString(CultureInfo.InvariantCulture),
        DateOnly date => date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
        string text => text,
        _ => throw new InvalidOperationException($"A cell of the export column '{column.Header}' has an unsupported type."),
    };
}
