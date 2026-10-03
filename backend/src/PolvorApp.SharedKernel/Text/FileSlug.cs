using System.Globalization;
using System.Text;

namespace PolvorApp.SharedKernel.Text;

/// <summary>
/// File-name parts built from names that are not personal data, such as a comparsa's
/// (add-exports design D5; add-distribution-planning design D6).
/// </summary>
public static class FileSlug
{
    /// <summary>"Comparsa Sintética Norte" → "comparsa-sintetica-norte": ASCII letters and digits only.</summary>
    public static string Of(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var builder = new StringBuilder(text.Length);
        foreach (var c in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsAsciiLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        return builder.ToString().Trim('-');
    }
}
