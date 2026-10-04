using System.Buffers.Binary;

namespace PolvorApp.Exports.Contracts;

/// <summary>
/// A form to print and sign on paper (add-distribution-planning, design D3), e.g. the pickup
/// authorisation: a header with an optional logo and a few lines, a title, a flow of blocks, and one
/// to three signature boxes kept together. The builder writes every word in the reader's language,
/// colons included; the renderer only lays them out. The constructor checks the parts and keeps its
/// own copy of the lists.
/// </summary>
public sealed class DocumentForm
{
    /// <summary>The most signature boxes that fit side by side.</summary>
    public const int MaxSignatures = 3;

    /// <param name="fileStem">The file name without extension; see <see cref="DocumentFileStem"/>.</param>
    /// <param name="title">The form's title.</param>
    /// <param name="headingLines">Lines next to the logo, e.g. the Federation's name.</param>
    /// <param name="blocks">The body, top to bottom.</param>
    /// <param name="signatureLabels">One signature box per label, side by side, after the body.</param>
    /// <param name="versionLine">The form's name and version, printed in the footer.</param>
    /// <param name="logo">The logo printed in the header, or null for none.</param>
    public DocumentForm(
        string fileStem,
        string title,
        IReadOnlyList<string> headingLines,
        IReadOnlyList<DocumentFormBlock> blocks,
        IReadOnlyList<string> signatureLabels,
        string versionLine,
        DocumentImage? logo)
    {
        DocumentFileStem.Check(fileStem, nameof(fileStem));
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(headingLines);
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(signatureLabels);
        ArgumentNullException.ThrowIfNull(versionLine);
        if (signatureLabels.Count is 0 or > MaxSignatures)
        {
            throw new ArgumentException($"A form has one to {MaxSignatures} signature boxes.", nameof(signatureLabels));
        }

        FileStem = fileStem;
        Title = title;
        HeadingLines = [.. headingLines.Select(line => line ?? throw new ArgumentNullException(nameof(headingLines)))];
        Blocks = [.. blocks.Select(block => block ?? throw new ArgumentNullException(nameof(blocks)))];
        SignatureLabels = [.. signatureLabels.Select(label => label ?? throw new ArgumentNullException(nameof(signatureLabels)))];
        VersionLine = versionLine;
        Logo = logo;
    }

    public string FileStem { get; }

    public string Title { get; }

    public IReadOnlyList<string> HeadingLines { get; }

    public IReadOnlyList<DocumentFormBlock> Blocks { get; }

    public IReadOnlyList<string> SignatureLabels { get; }

    public string VersionLine { get; }

    public DocumentImage? Logo { get; }

    /// <summary>The type name only: the fields are personal data.</summary>
    public override string ToString() => nameof(DocumentForm);
}

/// <summary>
/// One part of a form's body. Only the blocks below exist, the renderer knows each of them: a class, not
/// a record, because a record's copy constructor would let other assemblies derive from it.
/// </summary>
public abstract class DocumentFormBlock
{
    private protected DocumentFormBlock()
    {
    }
}

/// <summary>A bold heading that starts a part of the form, e.g. "Holder".</summary>
public sealed class DocumentFormHeading : DocumentFormBlock
{
    public DocumentFormHeading(string text) => Text = text ?? throw new ArgumentNullException(nameof(text));

    public string Text { get; }

    /// <summary>The type name only, as the other blocks.</summary>
    public override string ToString() => nameof(DocumentFormHeading);
}

/// <summary>A paragraph of running text, e.g. the statement the holder signs.</summary>
public sealed class DocumentFormParagraph : DocumentFormBlock
{
    public DocumentFormParagraph(string text) => Text = text ?? throw new ArgumentNullException(nameof(text));

    public string Text { get; }

    /// <summary>The type name only: a statement may name people.</summary>
    public override string ToString() => nameof(DocumentFormParagraph);
}

/// <summary>
/// A labelled value on a line. A null or empty value leaves the line blank, to be written by hand. The
/// label is printed as given, e.g. "Reason:".
/// </summary>
public sealed class DocumentFormField : DocumentFormBlock
{
    public DocumentFormField(string label, string? value)
    {
        Label = label ?? throw new ArgumentNullException(nameof(label));
        Value = value;
    }

    public string Label { get; }

    public string? Value { get; }

    /// <summary>The label only: the value is personal data.</summary>
    public override string ToString() => $"{nameof(DocumentFormField)} {Label}";
}

/// <summary>How a <see cref="DocumentImage"/> is encoded.</summary>
public enum DocumentImageFormat
{
    /// <summary>PNG, e.g. a logo with transparency.</summary>
    Png,

    /// <summary>JPEG, e.g. an ID photo (add-badges, design D2).</summary>
    Jpeg,
}

/// <summary>
/// A PNG or JPEG image to print, e.g. a logo or a photo. Built only from the encoded image itself, so
/// its size always matches its pixels: at least 1 and at most <see cref="MaxSide"/> pixels a side, at
/// most <see cref="MaxBytes"/>.
/// </summary>
public sealed class DocumentImage
{
    public const int MaxSide = 4096;

    public const int MaxBytes = 4 * 1024 * 1024;

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private DocumentImage(ReadOnlyMemory<byte> content, DocumentImageFormat format, int width, int height) =>
        (Content, Format, Width, Height) = (content, format, width, height);

    /// <summary>The encoded image, in <see cref="Format"/>.</summary>
    public ReadOnlyMemory<byte> Content { get; }

    /// <summary>Informational: the writers draw either kind, as QuestPDF reads the format from the bytes.</summary>
    public DocumentImageFormat Format { get; }

    /// <summary>Width in pixels, read from the image header.</summary>
    public int Width { get; }

    /// <summary>Height in pixels, read from the image header.</summary>
    public int Height { get; }

    /// <summary>A copy of <paramref name="png"/>, refused unless it is a PNG within the limits.</summary>
    public static DocumentImage FromPng(ReadOnlyMemory<byte> png)
    {
        var bytes = png.Span;
        // Signature (8), IHDR length (4) and type (4), then width and height (4 + 4), big-endian.
        if (bytes.Length < 24 || bytes.Length > MaxBytes || !bytes[..8].SequenceEqual(PngSignature) || !bytes[12..16].SequenceEqual("IHDR"u8))
        {
            throw new ArgumentException($"A document image is a PNG of at most {MaxBytes} bytes.", nameof(png));
        }

        var width = BinaryPrimitives.ReadInt32BigEndian(bytes[16..20]);
        var height = BinaryPrimitives.ReadInt32BigEndian(bytes[20..24]);
        return Create(png, DocumentImageFormat.Png, width, height, nameof(png));
    }

    /// <summary>
    /// A copy of <paramref name="jpeg"/>, refused unless it is a complete baseline, extended or progressive
    /// JPEG of 8 bits with 1 (grey) or 3 (colour) components, within the limits: the kinds the PDF writers
    /// draw as they are.
    /// </summary>
    public static DocumentImage FromJpeg(ReadOnlyMemory<byte> jpeg)
    {
        if (jpeg.Length > MaxBytes || JpegFrameSize(jpeg.Span) is not var (width, height))
        {
            throw new ArgumentException($"A document image is an 8-bit grey or colour JPEG of at most {MaxBytes} bytes.", nameof(jpeg));
        }

        return Create(jpeg, DocumentImageFormat.Jpeg, width, height, nameof(jpeg));
    }

    /// <summary>Its size only.</summary>
    public override string ToString() => $"{nameof(DocumentImage)} {Width}×{Height}";

    private static DocumentImage Create(ReadOnlyMemory<byte> content, DocumentImageFormat format, int width, int height, string parameter)
    {
        if (width is < 1 or > MaxSide || height is < 1 or > MaxSide)
        {
            throw new ArgumentException($"A document image is 1 to {MaxSide} pixels a side.", parameter);
        }

        return new DocumentImage(content.ToArray(), format, width, height);
    }

    /// <summary>
    /// The size in the frame header, walking the marker segments after the start-of-image marker; null
    /// unless the bytes end with the end-of-image marker (no truncated file) and the frame is baseline,
    /// extended sequential or progressive (SOF0–SOF2) with 8-bit samples and 1 or 3 components.
    /// </summary>
    private static (int Width, int Height)? JpegFrameSize(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 4 || bytes[0] != 0xFF || bytes[1] != 0xD8 || bytes[^2] != 0xFF || bytes[^1] != 0xD9)
        {
            return null;
        }

        var position = 2;
        while (position + 4 <= bytes.Length)
        {
            if (bytes[position] != 0xFF)
            {
                return null;
            }

            var marker = bytes[position + 1];
            if (marker == 0xFF)
            {
                position++; // fill byte
                continue;
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(bytes[(position + 2)..]);
            if (length < 2 || position + 2 + length > bytes.Length)
            {
                return null;
            }

            if (marker is >= 0xC0 and <= 0xCF and not (0xC4 or 0xC8 or 0xCC))
            {
                // Length (2), precision (1), height (2), width (2), components (1). Lossless, hierarchical and
                // arithmetic-coded frames, 12-bit samples and CMYK are refused.
                return marker <= 0xC2 && length >= 8 && bytes[position + 4] == 8 && bytes[position + 9] is 1 or 3
                    ? (BinaryPrimitives.ReadUInt16BigEndian(bytes[(position + 7)..]), BinaryPrimitives.ReadUInt16BigEndian(bytes[(position + 5)..]))
                    : null;
            }

            if (marker == 0xDA)
            {
                return null; // scan data before any frame header
            }

            position += 2 + length;
        }

        return null;
    }
}
