using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace PolvorApp.SharedKernel.Images;

/// <summary>
/// Writes plain 8-bit PNGs, unfiltered and compressed with zlib, for the synthetic seeds (SEC-11):
/// modules draw flat shapes pixel by pixel without an image library, then run the result through
/// <see cref="IImageNormalizer"/> like any upload.
/// </summary>
public static class SyntheticPng
{
    private const byte ColourTypeRgb = 2;
    private const byte ColourTypeRgba = 6;

    /// <summary>An opaque RGB image whose colour at (x, y) is <paramref name="pixel"/>.</summary>
    public static byte[] Rgb(int width, int height, Func<int, int, (byte R, byte G, byte B)> pixel) =>
        Encode(width, height, 3, ColourTypeRgb, (x, y, row) =>
        {
            var (r, g, b) = pixel(x, y);
            (row[0], row[1], row[2]) = (r, g, b);
        });

    /// <summary>An RGBA image (straight alpha) whose colour at (x, y) is <paramref name="pixel"/>.</summary>
    public static byte[] Rgba(int width, int height, Func<int, int, (byte R, byte G, byte B, byte A)> pixel) =>
        Encode(width, height, 4, ColourTypeRgba, (x, y, row) =>
        {
            var (r, g, b, a) = pixel(x, y);
            (row[0], row[1], row[2], row[3]) = (r, g, b, a);
        });

    private delegate void PixelWriter(int x, int y, Span<byte> target);

    private static byte[] Encode(int width, int height, int channels, byte colourType, PixelWriter write)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        var stride = checked((width * channels) + 1);
        var raw = new byte[checked(height * stride)];
        for (var y = 0; y < height; y++)
        {
            var row = raw.AsSpan(y * stride, stride);
            row[0] = 0; // filter: none
            for (var x = 0; x < width; x++)
            {
                write(x, y, row.Slice(1 + (x * channels), channels));
            }
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            zlib.Write(raw);
        }

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        (header[8], header[9]) = (8, colourType); // 8 bits per channel

        using var png = new MemoryStream();
        png.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);
        WriteChunk(png, "IHDR", header);
        WriteChunk(png, "IDAT", compressed.ToArray());
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
        stream.Write(number);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);
        stream.Write(data);
        BinaryPrimitives.WriteUInt32BigEndian(number, Crc32([.. typeBytes, .. data]));
        stream.Write(number);
    }

    private static uint Crc32(byte[] bytes)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return ~crc;
    }
}
