using Microsoft.Extensions.Time.Testing;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.Exports.Contracts;
using PolvorApp.Exports.Writers;
using UglyToad.PdfPig;

namespace PolvorApp.Api.Tests.Exports;

/// <summary>
/// Forms rendered as PDF through <see cref="IDocumentRenderer.RenderForm"/> (add-distribution-planning,
/// design D3): header with an optional logo, filled and blank fields, statements and signature boxes.
/// </summary>
public sealed class DocumentFormTests
{
    private static readonly DocumentRenderer Renderer = new(new FakeTimeProvider(new DateTimeOffset(2031, 3, 2, 9, 0, 0, TimeSpan.Zero)));

    [Fact]
    public void A_form_matches_its_golden_file()
    {
        var document = Renderer.RenderForm(Form(logo: null));

        Assert.Equal("application/pdf", document.ContentType);
        Assert.Equal("polvorapp-2031-test-form.pdf", document.FileName);
        Golden.Matches("form.pdf", DocumentText.Pdf(document.Content.ToArray()));
    }

    [Fact]
    public void A_form_with_a_logo_has_the_same_text_and_one_image()
    {
        var withLogo = Renderer.RenderForm(Form(Logo())).Content.ToArray();
        var without = Renderer.RenderForm(Form(logo: null)).Content.ToArray();

        Assert.Equal(DocumentText.Pdf(without), DocumentText.Pdf(withLogo));
        using var pdf = PdfDocument.Open(withLogo);
        var image = Assert.Single(pdf.GetPage(1).GetImages());
        // A fixed height, keeping the logo's 4:3 shape.
        Assert.Equal(PdfFormWriter.LogoHeight, image.BoundingBox.Height, 1);
        Assert.Equal(4.0 / 3.0, image.BoundingBox.Width / image.BoundingBox.Height, 2);
        using var plain = PdfDocument.Open(without);
        Assert.Empty(plain.GetPage(1).GetImages());
    }

    [Fact]
    public void A_form_is_one_a4_portrait_page()
    {
        using var pdf = PdfDocument.Open(Renderer.RenderForm(Form(Logo())).Content.ToArray());

        Assert.Equal(1, pdf.NumberOfPages);
        var page = pdf.GetPage(1);
        Assert.True(page.Height > page.Width);
        Assert.Equal(595, page.Width, 1);
    }

    [Fact]
    public void A_blank_field_prints_its_label_and_a_line_to_write_on()
    {
        using var pdf = PdfDocument.Open(Renderer.RenderForm(Form(logo: null)).Content.ToArray());

        Assert.Equal("Motivo:", DocumentText.Lines(pdf.GetPage(1)).Single(line => line.StartsWith("Motivo", StringComparison.Ordinal)));
    }

    [Fact]
    public void The_same_form_gives_the_same_file() =>
        Assert.Equal(Renderer.RenderForm(Form(Logo())).Content.ToArray(), Renderer.RenderForm(Form(Logo())).Content.ToArray());

    [Fact]
    public void A_form_never_prints_its_values()
    {
        Assert.Equal("DocumentForm", Form(logo: null).ToString());
        Assert.Equal("DocumentFormField DNI/NIE:", new DocumentFormField("DNI/NIE:", "00000000T").ToString());
    }

    private static DocumentImage Logo() => DocumentImage.FromPng(TestImages.Png(400, 300, transparent: true));

    private static DocumentForm Form(DocumentImage? logo) => new(
        "polvorapp-2031-test-form",
        "Autorización de prueba — Fiestas 2031",
        ["Federación Sintética de Comparsas"],
        [
            new DocumentFormHeading("Titular"),
            new DocumentFormField("Apellidos y nombre:", "Abad Sintético, Ana"),
            new DocumentFormField("DNI/NIE:", "00000000T"),
            new DocumentFormField("Licencia:", "AE"),
            new DocumentFormParagraph("No puede recoger el día del reparto, por el motivo que escribe a mano."),
            new DocumentFormField("Motivo:", null),
            new DocumentFormHeading("Autorizado"),
            new DocumentFormField("Apellidos y nombre:", "Zamora Sintética, Berta"),
            new DocumentFormField("Lugar y fecha:", null),
        ],
        ["Firma del titular", "Firma del autorizado"],
        "test-form, versión 1",
        logo);
}
