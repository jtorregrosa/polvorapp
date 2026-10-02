using System.Globalization;
using System.Text;

namespace PolvorApp.ArquebusierRegistry.Import;

/// <summary>Comparing what a person typed in a spreadsheet with the template's headers and values (design D3, D4).</summary>
internal static class ImportText
{
    /// <summary>
    /// Trimmed, inner spaces collapsed, lower-cased, without accents and with plain apostrophes:
    /// "  TELÉFONO " → "telefono".
    /// </summary>
    public static string Normalise(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var pendingSpace = false;
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            // A typographic apostrophe, as word processors type it, matches the plain one: "d’expedició".
            builder.Append(character is '\u2019' or '\u2018' ? '\'' : char.ToLowerInvariant(character));
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
