using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace PolvorApp.ArquebusierRegistry.Seeding;

/// <summary>
/// Placeholder images for the synthetic seed (spec: Synthetic registry data, SEC-11, design D10):
/// flat shapes only, never a face, a real document or any text. A silhouette on a coloured
/// background stands for an ID photo; a card with a band, a photo box and stripes stands for a
/// license side. They are plain PNGs built here, so the module needs no image library, and the seeder
/// runs them through the same normaliser as uploads.
/// </summary>
internal static class SyntheticPhotos
{
    public const int IdWidth = 600;
    public const int IdHeight = 800;
    public const int CardWidth = 1000;
    public const int CardHeight = 630;

    private static readonly (byte R, byte G, byte B)[] Palette =
    [
        (0xC8, 0xD8, 0xE8), (0xE8, 0xD8, 0xC8), (0xD8, 0xE8, 0xC8), (0xE0, 0xC8, 0xE0), (0xC8, 0xE0, 0xE0),
    ];

    private static readonly (byte R, byte G, byte B) Figure = (0x4A, 0x55, 0x68);
    private static readonly (byte R, byte G, byte B) Card = (0xF4, 0xF1, 0xEA);
    private static readonly (byte R, byte G, byte B) Band = (0x2F, 0x6B, 0x4F);
    private static readonly (byte R, byte G, byte B) Line = (0xB8, 0xB2, 0xA6);

    /// <summary>A 3:4 silhouette on a background chosen by <paramref name="variant"/>.</summary>
    public static byte[] IdPhoto(int variant)
    {
        var background = Palette[variant % Palette.Length];
        return Png(IdWidth, IdHeight, (x, y) =>
        {
            // Head: a circle; shoulders: the top half of a wide ellipse at the bottom.
            var head = Square(x - 300) + Square(y - 310) <= Square(130);
            var shoulders = y >= 520 && (Square(x - 300) / (double)Square(260)) + (Square(y - 800) / (double)Square(280)) <= 1;
            return head || shoulders ? Figure : background;
        });
    }

    /// <summary>A card side: front with a band and a photo box, back with stripes only.</summary>
    public static byte[] LicenseSide(bool front, int variant)
    {
        var accent = Palette[variant % Palette.Length];
        return Png(CardWidth, CardHeight, (x, y) =>
        {
            var border = x < 12 || y < 12 || x >= CardWidth - 12 || y >= CardHeight - 12;
            if (border)
            {
                return Line;
            }

            if (front && y < 110)
            {
                return Band;
            }

            if (front && x is >= 60 and < 300 && y is >= 160 and < 480)
            {
                return accent;
            }

            var stripeTop = front ? 180 : 80;
            var stripe = x >= (front ? 360 : 80) && x < CardWidth - 80 && y >= stripeTop && (y - stripeTop) % 70 < 22 && y < CardHeight - 60;
            return stripe ? Line : Card;
        });
    }

    private static int Square(int value) => value * value;

    /// <summary>An 8-bit RGB PNG, unfiltered, compressed with zlib.</summary>
    private static byte[] Png(int width, int height, Func<int, int, (byte R, byte G, byte B)> pixel)
    {
        var raw = new byte[height * ((width * 3) + 1)];
        var offset = 0;
        for (var y = 0; y < height; y++)
        {
            raw[offset++] = 0; // filter: none
            for (var x = 0; x < width; x++)
            {
                var (r, g, b) = pixel(x, y);
                (raw[offset], raw[offset + 1], raw[offset + 2]) = (r, g, b);
                offset += 3;
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
        (header[8], header[9]) = (8, 2); // 8 bits per channel, RGB

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
