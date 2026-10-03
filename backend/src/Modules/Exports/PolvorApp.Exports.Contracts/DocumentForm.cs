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

/// <summary>
/// A PNG image to print, e.g. a logo. Built only from the PNG itself, so its size always matches its
/// pixels: at least 1 and at most <see cref="MaxSide"/> pixels a side, at most <see cref="MaxBytes"/>.
/// </summary>
public sealed class DocumentImage
{
    public const int MaxSide = 4096;

    public const int MaxBytes = 4 * 1024 * 1024;

    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private DocumentImage(ReadOnlyMemory<byte> png, int width, int height) => (Png, Width, Height) = (png, width, height);

    /// <summary>The image, PNG-encoded.</summary>
    public ReadOnlyMemory<byte> Png { get; }

    /// <summary>Width in pixels, read from the PNG header.</summary>
    public int Width { get; }

    /// <summary>Height in pixels, read from the PNG header.</summary>
    public int Height { get; }

    /// <summary>A copy of <paramref name="png"/>, refused unless it is a PNG within the limits.</summary>
    public static DocumentImage FromPng(ReadOnlyMemory<byte> png)
    {
        var bytes = png.Span;
        // Signature (8), IHDR length (4) and type (4), then width and height (4 + 4), big-endian.
        if (bytes.Length < 24 || bytes.Length > MaxBytes || !bytes[..8].SequenceEqual(Signature) || !bytes[12..16].SequenceEqual("IHDR"u8))
        {
            throw new ArgumentException("A document image is a PNG of at most 4 MB.", nameof(png));
        }

        var width = BinaryPrimitives.ReadInt32BigEndian(bytes[16..20]);
        var height = BinaryPrimitives.ReadInt32BigEndian(bytes[20..24]);
        if (width is < 1 or > MaxSide || height is < 1 or > MaxSide)
        {
            throw new ArgumentException($"A document image is 1 to {MaxSide} pixels a side.", nameof(png));
        }

        return new DocumentImage(png.ToArray(), width, height);
    }

    /// <summary>Its size only.</summary>
    public override string ToString() => $"{nameof(DocumentImage)} {Width}×{Height}";
}
