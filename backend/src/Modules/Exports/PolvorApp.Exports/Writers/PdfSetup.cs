using PolvorApp.Exports.Contracts;
using QuestPDF;
using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace PolvorApp.Exports.Writers;

/// <summary>
/// QuestPDF's process-wide settings (design D4): the Community license (assessed in
/// docs/third-party-licenses.md), no system fonts — the runtime image has none, and the output must
/// not depend on the host — and the embedded Geist fonts. Applied once, before the first document.
/// Also what every PDF shares: its metadata and how a failure is reported.
/// </summary>
internal static class PdfSetup
{
    public const string FontFamily = "Geist";

    private static readonly Lazy<bool> Applied = new(Apply, LazyThreadSafetyMode.ExecutionAndPublication);

    public static void EnsureApplied() => _ = Applied.Value;

    /// <summary>Fixed dates: the same content on the same day gives the same file.</summary>
    public static DocumentMetadata Metadata(string title, DateOnly generatedOn)
    {
        var day = new DateTimeOffset(generatedOn.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        return new DocumentMetadata { Title = title, Author = "PolvorApp", Creator = "PolvorApp", Producer = "PolvorApp", CreationDate = day, ModifiedDate = day };
    }

    /// <summary>
    /// Generates the document. QuestPDF's messages quote the text they fail on (a missing glyph lists
    /// the letters of a name), so a failure becomes a <see cref="DocumentRenderingException"/> with a
    /// fixed message and no inner exception (group 2 review).
    /// </summary>
    public static byte[] Generate(string what, DocumentMetadata metadata, Action<IDocumentContainer> compose)
    {
        EnsureApplied();
        try
        {
            return Document.Create(compose).WithMetadata(metadata).GeneratePdf();
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or OutOfMemoryException))
        {
            throw DocumentRenderingException.For(what, exception);
        }
    }

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
