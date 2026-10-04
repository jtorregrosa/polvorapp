using System.Globalization;
using PolvorApp.Exports.Contracts;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PolvorApp.Exports.Writers;

/// <summary>
/// Writes a <see cref="DocumentForm"/> as an A4 portrait PDF (add-distribution-planning, design D3): the
/// logo and heading lines, the title, the blocks — a field's value on a line, or the line left blank to
/// write on — and the signature boxes, kept together on one page; the footer has the version line and
/// the generation date. Labels are printed as the builder wrote them. Geist only, as the exports, so
/// the output is the same on every host.
/// </summary>
internal static class PdfFormWriter
{
    /// <summary>The logo's height in points; its width follows its shape.</summary>
    public const float LogoHeight = 56;

    private const float LogoMaxWidth = 120;
    private const float FieldHeight = 20;
    private const float SignatureHeight = 72;

    public static byte[] Write(DocumentForm form, DateOnly generatedOn)
    {
        ArgumentNullException.ThrowIfNull(form);
        return PdfSetup.Generate("form", PdfSetup.Metadata(form.Title, generatedOn), document => document.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(40);
            page.DefaultTextStyle(style => style.FontFamily(PdfSetup.FontFamily).FontSize(10));
            if (form.Logo is not null || form.HeadingLines.Count > 0)
            {
                page.Header().Element(container => Header(container, form));
            }

            page.Content().PaddingVertical(12).Column(column =>
            {
                column.Spacing(6);
                column.Item().PaddingBottom(6).Text(form.Title).Bold().FontSize(14);
                foreach (var block in form.Blocks)
                {
                    Block(column.Item(), block);
                }

                // The boxes never split from each other across pages.
                column.Item().PaddingTop(24).ShowEntire().Row(row =>
                {
                    row.Spacing(24);
                    foreach (var label in form.SignatureLabels)
                    {
                        row.RelativeItem().Column(signature =>
                        {
                            signature.Item().Height(SignatureHeight).Border(0.75f);
                            signature.Item().PaddingTop(3).AlignCenter().Text(label);
                        });
                    }
                });
            });
            page.Footer().Row(row =>
            {
                row.RelativeItem().Text(form.VersionLine).FontSize(8).FontColor(Colors.Grey.Darken2);
                row.AutoItem().Text(generatedOn.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)).FontSize(8).FontColor(Colors.Grey.Darken2);
            });
        }));
    }

    private static void Header(IContainer container, DocumentForm form) => container.Row(row =>
    {
        row.Spacing(12);
        if (form.Logo is { } logo)
        {
            // The logo keeps its shape inside a fixed-height box; its size comes from the PNG itself.
            var width = Math.Min(LogoMaxWidth, LogoHeight * logo.Width / logo.Height);
            row.ConstantItem(width).Height(LogoHeight).Image(logo.Content.ToArray()).FitArea();
        }

        if (form.HeadingLines.Count > 0)
        {
            row.RelativeItem().AlignMiddle().Column(column =>
            {
                foreach (var line in form.HeadingLines)
                {
                    column.Item().Text(line).Bold();
                }
            });
        }
    });

    private static void Block(IContainer container, DocumentFormBlock block)
    {
        switch (block)
        {
            case DocumentFormHeading heading:
                container.PaddingTop(10).Text(heading.Text).Bold().FontSize(11);
                break;
            case DocumentFormParagraph paragraph:
                container.PaddingTop(4).Text(paragraph.Text);
                break;
            case DocumentFormField field:
                container.Row(row =>
                {
                    row.AutoItem().AlignBottom().PaddingRight(6).Text(field.Label);
                    // A blank value is a line to write on by hand.
                    row.RelativeItem().MinHeight(FieldHeight).BorderBottom(0.5f).AlignBottom().Text(field.Value ?? string.Empty);
                });
                break;
            default:
                // Unreachable: only this assembly derives blocks, and a test covers each of them.
                throw new InvalidOperationException($"Unknown form block {block.GetType().Name}.");
        }
    }
}
