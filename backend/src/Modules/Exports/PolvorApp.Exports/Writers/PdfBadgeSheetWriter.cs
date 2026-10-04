using System.Globalization;
using System.Text;
using PolvorApp.Exports.Contracts;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PolvorApp.Exports.Writers;

/// <summary>
/// Writes a <see cref="DocumentBadgeSheet"/> as A4 portrait pages of ten ISO/IEC 7810 ID-1 cards, two
/// columns by five rows, with no gap so one cut serves two cards (add-badges, design D2). The grid is
/// centred on a page without margins and crop marks continue each cutting line into the margins. Each
/// card has a header band reaching its edges, then, inside the 3 mm safe area, the logo and header
/// lines, the 3:4 photo slot, the labelled values and a free area for the hand-stamped seal. Values
/// shrink to fit their box, wrapping when needed, and are never cut. Geist only.
/// </summary>
internal static class PdfBadgeSheetWriter
{
    /// <summary>ID-1 card size in millimetres.</summary>
    public const float CardWidth = 85.60f;

    public const float CardHeight = 53.98f;

    /// <summary>The Federation's green, provisional until the Federation confirms it (design D2).</summary>
    public const string FederationGreen = "#1B5E3A";

    private const int Columns = 2;
    private const int Rows = 5;
    private const int CardsPerPage = Columns * Rows;
    private const float PageWidth = 210;
    private const float PageHeight = 297;
    private const float GridLeft = (PageWidth - Columns * CardWidth) / 2;
    private const float GridTop = (PageHeight - Rows * CardHeight) / 2;

    private const float SafeArea = 3;
    private const float BandHeight = 12;
    private const float LogoSize = BandHeight - SafeArea;
    private const float PhotoWidth = 20;
    private const float PhotoHeight = PhotoWidth * 4 / 3;
    private const float PhotoGap = 2.5f;
    private const float SealWidth = 22;
    private const float BodyGap = 2;
    private const float LabelHeight = 2.1f;
    private const float ValueHeight = 3.6f;

    private const float CropMarkOffset = 2;
    private const float CropMarkLength = 5;
    private const float CropMarkWidthPoints = 0.25f;
    private const float FrameWidthPoints = 0.5f;

    private const float PointsPerMillimetre = 72 / 25.4f;

    public static byte[] Write(DocumentBadgeSheet sheet, DateOnly generatedOn)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        var pages = sheet.Badges.Chunk(CardsPerPage).ToList();
        return PdfSetup.Generate("badges", PdfSetup.Metadata(sheet.Title, generatedOn), document =>
        {
            foreach (var badges in pages)
            {
                document.Page(page =>
                {
                    // Exactly 210 × 297 mm: QuestPDF's A4 is rounded to whole points, which would shift the grid.
                    page.Size(PageWidth, PageHeight, Unit.Millimetre);
                    page.Margin(0);
                    page.DefaultTextStyle(style => style.FontFamily(PdfSetup.FontFamily).FontSize(7).LineHeight(1));
                    page.Content().Layers(layers =>
                    {
                        layers.Layer().Svg(size => CropMarks(size, badges.Length));
                        layers.PrimaryLayer().Extend()
                            .PaddingLeft(GridLeft, Unit.Millimetre)
                            .PaddingTop(GridTop, Unit.Millimetre)
                            .Column(column =>
                            {
                                foreach (var row in badges.Chunk(Columns))
                                {
                                    column.Item().Height(CardHeight, Unit.Millimetre).Row(cards =>
                                    {
                                        foreach (var badge in row)
                                        {
                                            Card(cards.ConstantItem(CardWidth, Unit.Millimetre), sheet, badge);
                                        }
                                    });
                                }
                            });
                    });
                });
            }
        });
    }

    private static void Card(IContainer container, DocumentBadgeSheet sheet, DocumentBadge badge) => container.Layers(layers =>
    {
        // The band reaches the card's edges; everything else stays in the safe area.
        layers.Layer().Height(BandHeight, Unit.Millimetre).Background(FederationGreen);
        layers.PrimaryLayer().Padding(SafeArea, Unit.Millimetre).Column(column =>
        {
            column.Item().Height(BandHeight - SafeArea, Unit.Millimetre).Element(header => Header(header, sheet));
            column.Item().PaddingTop(BodyGap, Unit.Millimetre).Row(body =>
            {
                Photo(body.ConstantItem(PhotoWidth, Unit.Millimetre), badge.Photo);
                body.ConstantItem(PhotoGap, Unit.Millimetre);
                body.RelativeItem().Column(fields =>
                {
                    for (var index = 0; index < DocumentBadgeSheet.FieldCount; index++)
                    {
                        // Labels are the builder's words in any language: they shrink like the values, never overflow.
                        fields.Item().Height(LabelHeight, Unit.Millimetre).AlignBottom().ScaleToFit()
                            .Text(sheet.Labels[index]).FontSize(5).FontColor(Colors.Grey.Darken2);
                        Value(fields.Item().Height(ValueHeight, Unit.Millimetre), badge.Values[index]);
                    }
                });

                // The free area for the Federation's hand-stamped seal: nothing is drawn.
                body.ConstantItem(SealWidth, Unit.Millimetre);
            });
        });
    });

    private static void Header(IContainer container, DocumentBadgeSheet sheet) => container.Row(row =>
    {
        if (sheet.Logo is { } logo)
        {
            row.ConstantItem(LogoSize, Unit.Millimetre).Height(LogoSize, Unit.Millimetre).AlignMiddle().AlignCenter()
                .Image(logo.Content.ToArray()).FitArea();
            row.ConstantItem(PhotoGap, Unit.Millimetre);
        }

        row.RelativeItem().Column(lines =>
        {
            lines.Item().Height(5, Unit.Millimetre).AlignBottom().ScaleToFit()
                .Text(sheet.HeaderWord).Bold().FontSize(11).FontColor(Colors.White);
            lines.Item().Height(LogoSize - 5, Unit.Millimetre).AlignMiddle().ScaleToFit()
                .Text(sheet.HeaderLine).FontSize(5.5f).FontColor(Colors.White);
        });
    });

    private static void Photo(IContainer container, DocumentImage? photo)
    {
        var slot = container.Width(PhotoWidth, Unit.Millimetre).Height(PhotoHeight, Unit.Millimetre);
        if (photo is null)
        {
            slot.Border(FrameWidthPoints).BorderColor(Colors.Grey.Medium);
            return;
        }

        // Already scaled for print (design D3): QuestPDF's default resampling would drop below 300 dpi.
        slot.Image(photo.Content.ToArray()).FitArea().UseOriginalImage();
    }

    private static void Value(IContainer container, string? value)
    {
        if (value is null)
        {
            container.AlignBottom().PaddingBottom(0.5f, Unit.Millimetre).LineHorizontal(FrameWidthPoints).LineColor(Colors.Grey.Medium);
            return;
        }

        container.AlignMiddle().ScaleToFit().Text(value).Bold();
    }

    /// <summary>
    /// The page's crop marks as SVG in points: each vertical cutting line that bounds a printed card gets
    /// a mark above the grid and one below the last printed row, each such horizontal line one left and
    /// one right of it.
    /// </summary>
    private static string CropMarks(Size size, int cards)
    {
        var rows = (cards + Columns - 1) / Columns;
        var columns = Math.Min(cards, Columns);
        var svg = new StringBuilder();
        svg.Append(CultureInfo.InvariantCulture, $"""<svg xmlns="http://www.w3.org/2000/svg" width="{size.Width}" height="{size.Height}">""");
        var gridBottom = GridTop + rows * CardHeight;
        var gridRight = GridLeft + columns * CardWidth;
        for (var line = 0; line <= columns; line++)
        {
            var x = GridLeft + line * CardWidth;
            Line(svg, x, GridTop - CropMarkOffset - CropMarkLength, x, GridTop - CropMarkOffset);
            Line(svg, x, gridBottom + CropMarkOffset, x, gridBottom + CropMarkOffset + CropMarkLength);
        }

        for (var line = 0; line <= rows; line++)
        {
            var y = GridTop + line * CardHeight;
            Line(svg, GridLeft - CropMarkOffset - CropMarkLength, y, GridLeft - CropMarkOffset, y);
            Line(svg, gridRight + CropMarkOffset, y, gridRight + CropMarkOffset + CropMarkLength, y);
        }

        svg.Append("</svg>");
        return svg.ToString();
    }

    private static void Line(StringBuilder svg, float x1, float y1, float x2, float y2) => svg.Append(
        CultureInfo.InvariantCulture,
        $"""<line x1="{x1 * PointsPerMillimetre}" y1="{y1 * PointsPerMillimetre}" x2="{x2 * PointsPerMillimetre}" y2="{y2 * PointsPerMillimetre}" stroke="#000000" stroke-width="{CropMarkWidthPoints}"/>""");
}
