using QuestPDF;
using QuestPDF.Drawing;
using QuestPDF.Infrastructure;

namespace PolvorApp.Exports.Writers;

/// <summary>
/// QuestPDF's process-wide settings (design D4): the Community license (assessed in
/// docs/third-party-licenses.md), no system fonts — the runtime image has none, and the output must
/// not depend on the host — and the embedded Geist fonts. Applied once, before the first document.
/// </summary>
internal static class PdfSetup
{
    public const string FontFamily = "Geist";

    private static readonly Lazy<bool> Applied = new(Apply, LazyThreadSafetyMode.ExecutionAndPublication);

    public static void EnsureApplied() => _ = Applied.Value;

    private static bool Apply()
    {
        Settings.License = LicenseType.Community;
        Settings.UseSystemFonts = false;
        // A glyph Geist lacks fails the PDF (the Excel file still works) rather than print a wrong name.
        Settings.ThrowOnMissingTextGlyphs = true;
        FontManager.RegisterFontFromEmbeddedResource("PolvorApp.Exports.Fonts.Geist-Regular.ttf");
        FontManager.RegisterFontFromEmbeddedResource("PolvorApp.Exports.Fonts.Geist-Bold.ttf");
        return true;
    }
}
