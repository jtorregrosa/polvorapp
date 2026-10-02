using System.IO.Compression;
using System.Text;
using System.Xml;

namespace PolvorApp.ArquebusierRegistry.Import;

/// <summary>
/// Checks an uploaded package before ClosedXML loads it (spec: Import file reading; design D3).
/// ClosedXML and the Open XML SDK build whole documents in memory and recurse over nested elements,
/// so a small hostile file could exhaust memory or overflow the stack, which no <c>catch</c> can
/// stop. Every XML part is therefore read here first with a forward-only reader that never recurses:
/// it prohibits document types (no entity expansion), bounds the nesting depth, and counts the rows,
/// cells and shared strings. The sizes in zip headers are written by the sender, so the decompressed
/// bytes are counted as they are read.
/// </summary>
internal static class WorkbookPackage
{
    /// <summary>A template workbook has about 15 parts.</summary>
    public const int MaxEntries = 200;

    /// <summary>A 1000-row template holds well under 1 MB of XML; ClosedXML's model is many times larger.</summary>
    public const long MaxUncompressedBytes = 5 * 1024 * 1024;

    /// <summary>Real workbook parts nest less than 20 elements deep.</summary>
    public const int MaxDepth = 64;

    /// <summary>Row elements across every sheet: the 1000-row limit with room for other sheets of the template.</summary>
    public const int MaxRowElements = 5_000;

    public const int MaxCellElements = 100_000;

    public const int MaxSharedStrings = 20_000;

    private const string ContentTypesPart = "[Content_Types].xml";
    private const string WorkbookContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml";
    private const string SpreadsheetNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const int ContentTypesMaxBytes = 64 * 1024;

    private static readonly XmlReaderSettings XmlSettings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreComments = true,
        IgnoreWhitespace = true,
        IgnoreProcessingInstructions = true,
        CloseInput = false,
    };

    /// <summary>
    /// Null when <paramref name="content"/> is a macro-free xlsx workbook that is safe to load;
    /// otherwise why not, as a short code for the logs (never content).
    /// </summary>
    public static string? Rejection(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        try
        {
            using var archive = new ZipArchive(new MemoryStream(content, writable: false), ZipArchiveMode.Read);
            if (archive.Entries.Count > MaxEntries)
            {
                return "tooManyEntries";
            }

            if (archive.GetEntry(ContentTypesPart) is not { } contentTypes || !DeclaresWorkbook(contentTypes))
            {
                return "notWorkbook";
            }

            var budget = new Budget();
            foreach (var entry in archive.Entries)
            {
                if (Scan(entry, budget) is { } rejection)
                {
                    return rejection;
                }
            }

            return null;
        }
        catch (XmlException)
        {
            // Malformed XML, or a document type, which the reader prohibits.
            return "xml";
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Not a zip, a corrupt one, a compression method .NET does not read, or past the byte budget.
            return "package";
        }
    }

    /// <summary>
    /// The workbook part must be a plain workbook: a macro-enabled one (<c>xlsm</c>), a binary one
    /// (<c>xlsb</c>) or a template declares another content type.
    /// </summary>
    private static bool DeclaresWorkbook(ZipArchiveEntry contentTypes)
    {
        using var stream = new BoundedStream(contentTypes.Open(), ContentTypesMaxBytes);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var text = reader.ReadToEnd();
        return text.Contains(WorkbookContentType, StringComparison.Ordinal)
            && !text.Contains("macroEnabled", StringComparison.OrdinalIgnoreCase);
    }

    private static string? Scan(ZipArchiveEntry entry, Budget budget)
    {
        using var stream = new BoundedStream(entry.Open(), MaxUncompressedBytes - budget.Bytes);
        try
        {
            if (!IsXml(entry.FullName))
            {
                stream.CopyTo(Stream.Null);
                return null;
            }

            using var reader = XmlReader.Create(stream, XmlSettings);
            while (reader.Read())
            {
                if (reader.Depth > MaxDepth)
                {
                    return "tooDeep";
                }

                if (reader.NodeType == XmlNodeType.Element && reader.NamespaceURI == SpreadsheetNamespace && Count(reader.LocalName, budget) is { } rejection)
                {
                    return rejection;
                }
            }

            return null;
        }
        finally
        {
            budget.Bytes += stream.BytesRead;
        }
    }

    private static string? Count(string element, Budget budget) => element switch
    {
        "row" when ++budget.Rows > MaxRowElements => "tooManyRows",
        "c" when ++budget.Cells > MaxCellElements => "tooManyCells",
        "si" when ++budget.SharedStrings > MaxSharedStrings => "tooManySharedStrings",
        _ => null,
    };

    private static bool IsXml(string name) =>
        name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".vml", StringComparison.OrdinalIgnoreCase);

    /// <summary>What the parts read so far have used of the limits.</summary>
    private sealed class Budget
    {
        public long Bytes { get; set; }

        public int Rows { get; set; }

        public int Cells { get; set; }

        public int SharedStrings { get; set; }
    }

    /// <summary>A read-only stream that fails once more than <c>limit</c> bytes have been read from it.</summary>
    private sealed class BoundedStream(Stream inner, long limit) : Stream
    {
        public long BytesRead { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, count));

        public override int Read(Span<byte> buffer) => Count(inner.Read(buffer));

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }

        private int Count(int read)
        {
            BytesRead += read;
            return BytesRead > limit ? throw new InvalidDataException("The package decompresses past the limit.") : read;
        }
    }
}
