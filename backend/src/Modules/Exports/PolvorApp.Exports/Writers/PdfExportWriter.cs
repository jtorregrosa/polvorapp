using System.Globalization;
using PolvorApp.Exports.Definitions;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PolvorApp.Exports.Writers;

/// <summary>
/// Writes an <see cref="ExportTable"/> as a PDF (spec: Excel and PDF; design D4): A4, landscape above
/// six columns; the title, notices and version line, then the table whose header repeats on every
/// page; a footer with the generation date and "page / pages". Geist only, so the output is the same
/// on every host. Numbers are written without thousands separators (they are counts and IDs).
/// </summary>
internal static class PdfExportWriter
{
    public const string ContentType = "application/pdf";

    public const string Extension = "pdf";

    private const int PortraitMaxColumns = 6;
    private const float DateWidth = 62;
    private const float IntegerWidth = 56;

    public static byte[] Write(ExportTable table, DateOnly generatedOn)
    {
        ArgumentNullException.ThrowIfNull(table);
        PdfSetup.EnsureApplied();
        var landscape = table.Columns.Count > PortraitMaxColumns;
        // Fixed dates: the same table on the same day gives the same file.
        var day = new DateTimeOffset(generatedOn.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var metadata = new DocumentMetadata { Title = table.Title, Author = "PolvorApp", Creator = "PolvorApp", Producer = "PolvorApp", CreationDate = day, ModifiedDate = day };
        return Document.Create(document => document.Page(page =>
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
        })).WithMetadata(metadata).GeneratePdf();
    }

    private static void Table(IContainer container, ExportTable table) => container.Table(grid =>
    {
        grid.ColumnsDefinition(columns =>
        {
            // Dates and numbers get fixed widths that fit "31/05/2033" and a six-digit ID unbroken;
            // text columns share the rest.
            foreach (var column in table.Columns)
            {
                switch (column.Type)
                {
                    case ExportCellType.Date:
                        columns.ConstantColumn(DateWidth);
                        break;
                    case ExportCellType.Integer:
                        columns.ConstantColumn(IntegerWidth);
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
        foreach (var row in table.Rows)
        {
            Row(grid, table.Columns, row, bold: false);
        }

        if (table.TotalRow is { } total)
        {
            Row(grid, table.Columns, total, bold: true);
        }
    });

    private static void Row(TableDescriptor grid, IReadOnlyList<ExportColumn> columns, IReadOnlyList<object?> cells, bool bold)
    {
        for (var c = 0; c < cells.Count; c++)
        {
            var cell = grid.Cell().BorderBottom(0.25f).BorderColor(Colors.Grey.Lighten2).Padding(3);
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

    private static string Format(ExportColumn column, object? value) => value switch
    {
        null => string.Empty,
        int number => number.ToString(CultureInfo.InvariantCulture),
        DateOnly date => date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
        string text => text,
        _ => throw new InvalidOperationException($"A cell of the export column '{column.Header}' has an unsupported type."),
    };
}
