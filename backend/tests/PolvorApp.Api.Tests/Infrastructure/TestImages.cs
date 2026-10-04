using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using SkiaSharp;

namespace PolvorApp.Api.Tests.Infrastructure;

/// <summary>
/// Synthetic images generated in memory for photo tests: flat colours and quadrants, never real
/// photos or documents (SEC-11). Helpers add EXIF metadata and read JPEG markers.
/// </summary>
public static class TestImages
{
    public static readonly SKColor TopLeft = SKColors.Red;
    public static readonly SKColor TopRight = SKColors.Lime;
    public static readonly SKColor BottomLeft = SKColors.Blue;
    public static readonly SKColor BottomRight = SKColors.Yellow;

    /// <summary>A JPEG whose four quadrants have the four test colours.</summary>
    public static byte[] Jpeg(int width, int height) => Encode(Quadrants(width, height), SKEncodedImageFormat.Jpeg);

    public static byte[] Png(int width, int height, bool transparent = false) =>
        Encode(Quadrants(width, height, transparent), SKEncodedImageFormat.Png);

    public static byte[] Webp(int width, int height) => Encode(Quadrants(width, height), SKEncodedImageFormat.Webp);

    /// <summary>The first bytes of a PDF: a document, not an image.</summary>
    public static byte[] PdfHeader() => Encoding.ASCII.GetBytes("%PDF-1.7\n%âãÏÓ\n1 0 obj\n<< /Type /Catalog >>\nendobj\n");

    /// <summary>A PNG signature and header announcing <paramref name="width"/> × <paramref name="height"/>, without pixel data.</summary>
    public static byte[] PngHeaderOnly(int width, int height)
    {
        using var stream = new MemoryStream();
        stream.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8; // bit depth
        header[9] = 2; // RGB
        WriteChunk(stream, "IHDR", header);

        // A tiny, valid zlib stream: the decoder reads the header, but the pixels are missing.
        using (var compressed = new MemoryStream())
        {
            using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
            {
                zlib.Write(new byte[16]);
            }

            WriteChunk(stream, "IDAT", compressed.ToArray());
        }

        WriteChunk(stream, "IEND", []);
        return stream.ToArray();
    }

    /// <summary>
    /// A JPEG start marker and frame header announcing <paramref name="width"/> × <paramref name="height"/>,
    /// without scan data: baseline (SOF0), 8 bits and 3 components unless told otherwise, and an end marker
    /// unless <paramref name="complete"/> is false.
    /// </summary>
    public static byte[] JpegHeaderOnly(int width, int height, byte frameMarker = 0xC0, byte precision = 8, byte components = 3, bool complete = true)
    {
        var frame = new byte[] { 0xFF, frameMarker, 0x00, 0x11, precision, 0, 0, 0, 0, components, 1, 0x11, 0, 2, 0x11, 0, 3, 0x11, 0 };
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(5), (ushort)height);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(7), (ushort)width);
        return complete ? [0xFF, 0xD8, .. frame, 0xFF, 0xD9] : [0xFF, 0xD8, .. frame];
    }

    /// <summary>
    /// A JPEG that stores the upright quadrant image transformed so that a viewer applying EXIF
    /// <paramref name="orientation"/> (1–8) shows it upright, with GPS and camera tags.
    /// </summary>
    public static byte[] JpegWithExif(int uprightWidth, int uprightHeight, int orientation = 1)
    {
        using var upright = Quadrants(uprightWidth, uprightHeight);
        using var stored = Unorient(upright, orientation);
        return InsertExif(Encode(stored, SKEncodedImageFormat.Jpeg), orientation);
    }

    /// <summary>
    /// A JPEG carrying every kind of metadata segment a camera or editor writes: EXIF (APP1), XMP
    /// (APP1), an ICC profile (APP2), IPTC (APP13) and a comment (COM), plus bytes after its end.
    /// </summary>
    public static byte[] JpegWithAllMetadata(int width, int height)
    {
        var jpeg = InsertExif(Encode(Quadrants(width, height), SKEncodedImageFormat.Jpeg), 1);
        byte[] segments =
        [
            .. Segment(0xE1, [.. "http://ns.adobe.com/xap/1.0/\0"u8, .. "<x:xmpmeta>SyntheticXmp</x:xmpmeta>"u8]),
            .. Segment(0xE2, [.. "ICC_PROFILE\0"u8, 1, 1, .. new byte[64]]),
            .. Segment(0xED, [.. "Photoshop 3.0\0"u8, .. "SyntheticIptc"u8]),
            .. Segment(0xFE, [.. "SyntheticComment"u8]),
        ];
        return [.. jpeg[..2], .. segments, .. jpeg[2..], .. "TRAILING-SYNTHETIC-PAYLOAD"u8];
    }

    /// <summary>The APPn markers (0xE0–0xEF) and COM segments of a JPEG, up to the start of scan.</summary>
    public static IReadOnlyList<byte> JpegMarkers(ReadOnlySpan<byte> jpeg)
    {
        var markers = new List<byte>();
        var position = 2; // after SOI
        while (position + 4 <= jpeg.Length && jpeg[position] == 0xFF)
        {
            var marker = jpeg[position + 1];
            if (marker == 0xDA)
            {
                break;
            }

            markers.Add(marker);
            position += 2 + BinaryPrimitives.ReadUInt16BigEndian(jpeg[(position + 2)..]);
        }

        return markers;
    }

    /// <summary>The colour at a point of a decoded image.</summary>
    public static SKColor PixelAt(ReadOnlySpan<byte> image, int x, int y)
    {
        using var bitmap = SKBitmap.Decode(image);
        return bitmap.GetPixel(x, y);
    }

    /// <summary>Whether two colours are equal within JPEG noise.</summary>
    public static bool Near(SKColor actual, SKColor expected) =>
        Math.Abs(actual.Red - expected.Red) < 40 && Math.Abs(actual.Green - expected.Green) < 40 && Math.Abs(actual.Blue - expected.Blue) < 40;

    private static SKBitmap Quadrants(int width, int height, bool transparent = false)
    {
        var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, transparent ? SKAlphaType.Unpremul : SKAlphaType.Opaque);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(transparent ? SKColors.Transparent : SKColors.White);
        var (halfWidth, halfHeight) = (width / 2f, height / 2f);
        using var paint = new SKPaint();
        foreach (var (color, x, y) in new[] { (TopLeft, 0f, 0f), (TopRight, halfWidth, 0f), (BottomLeft, 0f, halfHeight), (BottomRight, halfWidth, halfHeight) })
        {
            paint.Color = transparent ? color.WithAlpha(128) : color;
            canvas.DrawRect(x, y, halfWidth, halfHeight, paint);
        }

        return bitmap;
    }

    private static byte[] Encode(SKBitmap bitmap, SKEncodedImageFormat format)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 95);
        return data.ToArray();
    }

    /// <summary>The inverse of the EXIF orientation transform: what a camera would store.</summary>
    private static SKBitmap Unorient(SKBitmap upright, int orientation)
    {
        var (w, h) = (upright.Width, upright.Height);
        var swap = orientation >= 5;
        var stored = new SKBitmap(swap ? h : w, swap ? w : h, upright.ColorType, upright.AlphaType);
        using var canvas = new SKCanvas(stored);

        // Maps upright (x, y) to stored coordinates.
        var matrix = orientation switch
        {
            2 => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),
            3 => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1),
            4 => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),
            5 => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
            6 => new SKMatrix(0, 1, 0, -1, 0, w, 0, 0, 1),
            7 => new SKMatrix(0, -1, h, -1, 0, w, 0, 0, 1),
            8 => new SKMatrix(0, -1, h, 1, 0, 0, 0, 0, 1),
            _ => SKMatrix.Identity,
        };
        canvas.SetMatrix(matrix);
        using var image = SKImage.FromBitmap(upright);
        canvas.DrawImage(image, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest), null);
        return stored;
    }

    /// <summary>Inserts an APP1 Exif segment (orientation, camera make and a GPS position) after SOI.</summary>
    private static byte[] InsertExif(byte[] jpeg, int orientation)
    {
        var tiff = new List<byte>();
        tiff.AddRange("MM"u8.ToArray());
        tiff.AddRange(BigEndian16(42));
        tiff.AddRange(BigEndian32(8));

        // IFD0: Make, Orientation, GPS IFD pointer.
        const int ifd0Entries = 3;
        var ifd0Size = 2 + (ifd0Entries * 12) + 4;
        var makeOffset = 8 + ifd0Size;
        var make = "SyntheticCam\0"u8.ToArray();
        var gpsOffset = makeOffset + make.Length;
        tiff.AddRange(BigEndian16(ifd0Entries));
        tiff.AddRange(Entry(0x010F, 2, (uint)make.Length, (uint)makeOffset));
        tiff.AddRange(Entry(0x0112, 3, 1, (uint)orientation << 16));
        tiff.AddRange(Entry(0x8825, 4, 1, (uint)gpsOffset));
        tiff.AddRange(BigEndian32(0));
        tiff.AddRange(make);

        // GPS IFD: latitude reference "N" and longitude reference "W" (values inline).
        tiff.AddRange(BigEndian16(2));
        tiff.AddRange(Entry(0x0001, 2, 2, 0x4E000000));
        tiff.AddRange(Entry(0x0003, 2, 2, 0x57000000));
        tiff.AddRange(BigEndian32(0));

        var payload = "Exif\0\0"u8.ToArray().Concat(tiff).ToArray();
        var segment = new List<byte> { 0xFF, 0xE1 };
        segment.AddRange(BigEndian16(payload.Length + 2));
        segment.AddRange(payload);
        return [.. jpeg[..2], .. segment, .. jpeg[2..]];
    }

    private static byte[] Segment(byte marker, byte[] payload) => [0xFF, marker, .. BigEndian16(payload.Length + 2), .. payload];

    private static byte[] Entry(ushort tag, ushort type, uint count, uint value) =>
        [.. BigEndian16(tag), .. BigEndian16(type), .. BigEndian32(count), .. BigEndian32(value)];

    private static byte[] BigEndian16(int value)
    {
        var bytes = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, (ushort)value);
        return bytes;
    }

    private static byte[] BigEndian32(uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        return bytes;
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        stream.Write(length);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);
        stream.Write(data);
        Span<byte> crc = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32([.. typeBytes, .. data]));
        stream.Write(crc);
    }

    private static uint Crc32(byte[] bytes)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in bytes)
        {
            crc ^= b;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return ~crc;
    }
}
