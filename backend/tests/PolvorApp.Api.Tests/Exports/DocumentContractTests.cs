using System.Reflection;
using Microsoft.Extensions.Time.Testing;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.Exports.Contracts;
using PolvorApp.Exports.Writers;

namespace PolvorApp.Api.Tests.Exports;

/// <summary>
/// The rules the public document types enforce when they are built (add-distribution-planning, design
/// D3; group 2 review): file names without personal data, well-formed tables and forms, real PNG
/// logos, and rendering failures that never echo the content.
/// </summary>
public sealed class DocumentContractTests
{
    private static readonly DocumentRenderer Renderer = new(new FakeTimeProvider(new DateTimeOffset(2031, 3, 2, 9, 0, 0, TimeSpan.Zero)));

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Polvorapp-2031")]
    [InlineData("polvorapp 2031")]
    [InlineData("../polvorapp")]
    [InlineData("polvorapp/2031")]
    [InlineData("polvorapp--2031")]
    [InlineData("-polvorapp")]
    [InlineData("polvorapp-")]
    [InlineData("polvorapp-2031\r\nx")]
    [InlineData("polvorapp-ñ")]
    public void A_table_refuses_a_file_stem_that_is_not_a_slug(string stem)
    {
        var error = Assert.Throws<ArgumentException>(() => Table(stem));

        // The message names the rule, never the value: a stem could be a name.
        Assert.Equal("fileStem", error.ParamName);
        Assert.DoesNotContain("polvorapp", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_file_stem_has_at_most_120_characters()
    {
        Assert.Throws<ArgumentException>(() => Table(new string('a', 121)));
        Assert.Equal(120, Table(new string('a', 120)).FileStem.Length);
    }

    [Fact]
    public void A_table_needs_its_texts_and_at_least_one_column()
    {
        Assert.Throws<ArgumentNullException>(() => new DocumentTable("stem", null!, [], "v", [Text("Nombre")], [], null));
        Assert.Throws<ArgumentNullException>(() => new DocumentTable("stem", "Título", null!, "v", [Text("Nombre")], [], null));
        Assert.Throws<ArgumentNullException>(() => new DocumentTable("stem", "Título", [], null!, [Text("Nombre")], [], null));
        Assert.Throws<ArgumentException>(() => new DocumentTable("stem", "Título", [], "v", [], [], null));
        Assert.Throws<ArgumentNullException>(() => new DocumentTable("stem", "Título", [], "v", [Text("Nombre")], [null!], null));
    }

    [Fact]
    public void A_column_must_have_a_known_type()
    {
        var error = Assert.Throws<ArgumentException>(() => new DocumentTable("stem", "Título", [], "v", [new("Raro", (DocumentCellType)99)], [], null));

        Assert.Contains("Raro", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_handwriting_column_is_text_and_stays_empty()
    {
        Assert.Throws<ArgumentException>(() => new DocumentTable("stem", "Título", [], "v", [new("Nº", DocumentCellType.Number, ForHandwriting: true)], [], null));
        var filled = Assert.Throws<InvalidOperationException>(() =>
            new DocumentTable("stem", "Título", [], "v", [Text("Nombre"), new("Firma", DocumentCellType.Text, ForHandwriting: true)], [["Ana", "12"]], null));

        Assert.Contains("Firma", filled.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("12", filled.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Only_the_total_row_may_put_text_in_a_number_column()
    {
        DocumentColumn[] columns = [new("Kg", DocumentCellType.Number), new("Cajas", DocumentCellType.Number)];

        Assert.Equal("Total", new DocumentTable("stem", "Título", [], "v", columns, [[1, 2]], ["Total", 2]).TotalRow![0]);
        Assert.Throws<InvalidOperationException>(() => new DocumentTable("stem", "Título", [], "v", columns, [["Uno", 2]], null));
    }

    [Fact]
    public void A_table_keeps_its_own_copy_of_the_rows()
    {
        var row = new List<object?> { "Abad Sintético, Ana" };
        var rows = new List<IReadOnlyList<object?>> { row };
        var table = new DocumentTable("stem", "Título", [], "v", [Text("Nombre")], rows, null);

        rows.Add(["Zamora Sintética, Berta"]);
        row[0] = 7;

        Assert.Equal("Abad Sintético, Ana", Assert.Single(table.Rows)[0]);
    }

    [Fact]
    public void A_form_refuses_a_file_stem_that_is_not_a_slug_and_needs_one_to_three_signatures()
    {
        Assert.Throws<ArgumentException>(() => Form("Formulario de Ana", ["Firma"]));
        Assert.Throws<ArgumentException>(() => Form("stem", []));
        Assert.Throws<ArgumentException>(() => Form("stem", ["1", "2", "3", "4"]));
        Assert.Equal(3, Form("stem", ["1", "2", "3"]).SignatureLabels.Count);
    }

    [Fact]
    public void Form_blocks_cannot_be_extended_by_other_modules() =>
        Assert.All(
            typeof(DocumentFormBlock).GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
            constructor => Assert.True(constructor.IsFamilyAndAssembly, "Only the contract assembly may derive form blocks."));

    [Fact]
    public void Every_form_block_is_rendered()
    {
        var blocks = typeof(DocumentFormBlock).Assembly.GetTypes().Where(t => t.IsSubclassOf(typeof(DocumentFormBlock)) && !t.IsAbstract).ToList();
        DocumentFormBlock[] known = [new DocumentFormHeading("Titular"), new DocumentFormParagraph("Texto."), new DocumentFormField("Motivo:", null)];

        Assert.Equal(blocks.Order(TypeComparer), known.Select(b => b.GetType()).Order(TypeComparer));
        Assert.Equal(1, PdfPageCount(Renderer.RenderForm(Form("stem", ["Firma"], known)).Content.ToArray()));
    }

    [Fact]
    public void A_logo_takes_its_size_from_the_png()
    {
        var logo = DocumentImage.FromPng(TestImages.Png(400, 300, transparent: true));

        Assert.Equal((400, 300), (logo.Width, logo.Height));
        Assert.Equal("DocumentImage 400×300", logo.ToString());
    }

    [Fact]
    public void A_logo_must_be_a_png_of_a_sensible_size()
    {
        Assert.Throws<ArgumentException>(() => DocumentImage.FromPng(ReadOnlyMemory<byte>.Empty));
        Assert.Throws<ArgumentException>(() => DocumentImage.FromPng(TestImages.Jpeg(400, 300)));
        Assert.Throws<ArgumentException>(() => DocumentImage.FromPng(TestImages.PngHeaderOnly(5000, 300)));
        Assert.Throws<ArgumentException>(() => DocumentImage.FromPng(TestImages.PngHeaderOnly(0, 300)));
    }

    [Fact]
    public void A_photo_takes_its_size_and_format_from_the_jpeg()
    {
        var photo = DocumentImage.FromJpeg(TestImages.Jpeg(300, 400));

        Assert.Equal((300, 400), (photo.Width, photo.Height));
        Assert.Equal(DocumentImageFormat.Jpeg, photo.Format);
        Assert.Equal(DocumentImageFormat.Png, DocumentImage.FromPng(TestImages.Png(4, 3)).Format);
        Assert.Equal("DocumentImage 300×400", photo.ToString());
    }

    [Fact]
    public void A_photo_must_be_a_jpeg_of_a_sensible_size()
    {
        Assert.Throws<ArgumentException>(() => DocumentImage.FromJpeg(ReadOnlyMemory<byte>.Empty));
        Assert.Throws<ArgumentException>(() => DocumentImage.FromJpeg(TestImages.Png(300, 400)));
        Assert.Throws<ArgumentException>(() => DocumentImage.FromJpeg(TestImages.JpegHeaderOnly(5000, 300)));
        Assert.Throws<ArgumentException>(() => DocumentImage.FromJpeg(TestImages.JpegHeaderOnly(300, 0)));
        Assert.Throws<ArgumentException>(() => DocumentImage.FromJpeg(new byte[DocumentImage.MaxBytes + 1]));
        var header = DocumentImage.FromJpeg(TestImages.JpegHeaderOnly(300, 400));
        Assert.Equal((300, 400), (header.Width, header.Height));
    }

    [Fact]
    public void A_truncated_jpeg_is_refused()
    {
        var jpeg = TestImages.Jpeg(300, 400);

        Assert.Throws<ArgumentException>(() => DocumentImage.FromJpeg(jpeg.AsMemory(0, jpeg.Length - 2)));
        Assert.Throws<ArgumentException>(() => DocumentImage.FromJpeg(TestImages.JpegHeaderOnly(300, 400, complete: false)));
    }

    [Theory]
    [InlineData(0xC3, 8, 3)] // lossless
    [InlineData(0xC5, 8, 3)] // hierarchical
    [InlineData(0xC9, 8, 3)] // arithmetic-coded
    [InlineData(0xC0, 12, 3)] // 12-bit samples
    [InlineData(0xC0, 8, 4)] // CMYK
    public void A_jpeg_the_pdf_writers_cannot_draw_as_is_is_refused(byte frameMarker, byte precision, byte components) =>
        Assert.Throws<ArgumentException>(() => DocumentImage.FromJpeg(TestImages.JpegHeaderOnly(300, 400, frameMarker, precision, components)));

    [Theory]
    [InlineData(0xC1, 3)]
    [InlineData(0xC2, 3)]
    [InlineData(0xC0, 1)]
    public void Extended_progressive_and_grey_jpegs_are_accepted(byte frameMarker, byte components) =>
        Assert.Equal(300, DocumentImage.FromJpeg(TestImages.JpegHeaderOnly(300, 400, frameMarker, 8, components)).Width);

    [Fact]
    public void A_form_draws_a_jpeg_logo()
    {
        var form = new DocumentForm(
            "stem", "Título", [], [new DocumentFormField("Nombre", "Ana")], ["Firma"], "v", DocumentImage.FromJpeg(TestImages.Jpeg(400, 300)));

        using var pdf = UglyToad.PdfPig.PdfDocument.Open(Renderer.RenderForm(form).Content.ToArray());

        Assert.Single(pdf.GetPage(1).GetImages());
    }

    [Fact]
    public void A_text_the_fonts_cannot_draw_fails_without_echoing_it()
    {
        var table = new DocumentTable("stem", "Título", [], "v", [Text("Nombre")], [["漢字 Sintético"]], null);

        var error = Assert.Throws<DocumentRenderingException>(() => Renderer.RenderTable(table, DocumentFileFormat.Pdf));

        Assert.DoesNotContain("漢", error.Message, StringComparison.Ordinal);
        Assert.Null(error.InnerException);
        Assert.True(error.TextUnprintable);
        Assert.Throws<DocumentRenderingException>(() => Renderer.RenderForm(Form("stem", ["Firma"], [new DocumentFormField("Nombre:", "漢字")])));
    }

    [Fact]
    public void An_unknown_format_is_refused() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Renderer.RenderTable(Table("stem"), (DocumentFileFormat)9));

    private static readonly Comparer<Type> TypeComparer = Comparer<Type>.Create((a, b) => string.CompareOrdinal(a.FullName, b.FullName));

    private static DocumentColumn Text(string header) => new(header, DocumentCellType.Text);

    private static DocumentTable Table(string stem) => new(stem, "Título", [], "v", [Text("Nombre")], [["Abad Sintético, Ana"]], null);

    private static DocumentForm Form(string stem, IReadOnlyList<string> signatures, IReadOnlyList<DocumentFormBlock>? blocks = null) =>
        new(stem, "Título", ["Federación Sintética"], blocks ?? [new DocumentFormField("Motivo:", null)], signatures, "form, versión 1", null);

    private static int PdfPageCount(byte[] pdf)
    {
        using var document = UglyToad.PdfPig.PdfDocument.Open(pdf);
        return document.NumberOfPages;
    }
}
