namespace PolvorApp.SharedKernel.Fonts;

/// <summary>The fonts embedded in the shared kernel.</summary>
public enum EmbeddedFont
{
    GeistRegular,
    GeistBold,
}

/// <summary>
/// Geist Regular and Bold (SIL OFL 1.1, license text in <c>Fonts/Geist-OFL.txt</c> next to the
/// assembly), embedded because the runtime image has no system fonts. The PDF exports register
/// them with QuestPDF and the seed's specimen license cards draw with them (realistic-seed-data,
/// design D6), so both use one copy.
/// </summary>
public static class EmbeddedFonts
{
    /// <summary>A new stream over the font file; the caller disposes it.</summary>
    public static Stream Open(EmbeddedFont font)
    {
        var name = font switch
        {
            EmbeddedFont.GeistRegular => "PolvorApp.SharedKernel.Fonts.Geist-Regular.ttf",
            EmbeddedFont.GeistBold => "PolvorApp.SharedKernel.Fonts.Geist-Bold.ttf",
            _ => throw new ArgumentOutOfRangeException(nameof(font), font, "Unknown embedded font."),
        };
        return typeof(EmbeddedFonts).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"The embedded font resource '{name}' is missing from the build.");
    }
}
