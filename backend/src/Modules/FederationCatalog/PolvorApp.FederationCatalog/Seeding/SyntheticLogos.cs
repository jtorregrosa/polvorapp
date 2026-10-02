using PolvorApp.SharedKernel.Images;

namespace PolvorApp.FederationCatalog.Seeding;

/// <summary>
/// Abstract emblems for the synthetic seed (spec: Synthetic catalogue data, SEC-11, design D9): flat
/// shapes on a transparent background, never text, letters or a real comparsa's emblem. The
/// crescent is near-black, so the light logo tile is exercised in the dark theme.
/// </summary>
internal static class SyntheticLogos
{
    private static readonly (byte R, byte G, byte B, byte A) Clear = (0, 0, 0, 0);
    private static readonly (byte R, byte G, byte B, byte A) NearBlack = (0x1C, 0x1C, 0x1E, 0xFF);
    private static readonly (byte R, byte G, byte B, byte A) Green = (0x2F, 0x6B, 0x4F, 0xFF);
    private static readonly (byte R, byte G, byte B, byte A) Gold = (0xD9, 0xA4, 0x41, 0xFF);
    private static readonly (byte R, byte G, byte B, byte A) Crimson = (0x8E, 0x2C, 0x3A, 0xFF);
    private static readonly (byte R, byte G, byte B, byte A) Cream = (0xF4, 0xEC, 0xD8, 0xFF);

    /// <summary>A near-black crescent, 512 × 512.</summary>
    public static byte[] Crescent() => SyntheticPng.Rgba(512, 512, (x, y) =>
        InCircle(x, y, 256, 256, 200) && !InCircle(x, y, 330, 220, 170) ? NearBlack : Clear);

    /// <summary>A green disc crossed by a gold band, 512 × 512.</summary>
    public static byte[] BandedDisc() => SyntheticPng.Rgba(512, 512, (x, y) =>
        !InCircle(x, y, 256, 256, 220) ? Clear : y is >= 220 and < 292 ? Gold : Green);

    /// <summary>A crimson diamond with a cream centre, 600 × 400.</summary>
    public static byte[] Diamond() => SyntheticPng.Rgba(600, 400, (x, y) =>
    {
        var distance = (Math.Abs(x - 300) / 280.0) + (Math.Abs(y - 200) / 180.0);
        return distance > 1 ? Clear : distance < 0.45 ? Cream : Crimson;
    });

    private static bool InCircle(int x, int y, int centreX, int centreY, int radius) =>
        ((x - centreX) * (x - centreX)) + ((y - centreY) * (y - centreY)) <= radius * radius;
}
