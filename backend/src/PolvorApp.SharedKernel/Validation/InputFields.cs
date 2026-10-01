using System.Globalization;
using System.Text;
using PolvorApp.SharedKernel.Codes;

namespace PolvorApp.FederationCatalog;

/// <summary>
/// Validation of catalogue requests (design D4): invalid fields are collected by name with a reason
/// code, so the UI can point at them. Texts are trimmed and normalised to NFC, so a decomposed
/// accent cannot bypass case-insensitive uniqueness, and invisible or control characters are
/// rejected, so two names cannot look identical and differ only by a hidden character.
/// </summary>
internal static class CatalogInput
{
    public const string Required = "required";
    public const string Invalid = "invalid";
    public const string TooLong = "tooLong";

    /// <summary>A required single-line text of at most <paramref name="maxLength"/> characters, trimmed and in NFC.</summary>
    public static string? Text(string? value, string field, int maxLength, IDictionary<string, string> errors)
    {
        if (value is not null && !IsPrintable(value))
        {
            errors[field] = Invalid;
            return null;
        }

        var text = value?.Normalize(NormalizationForm.FormC).Trim();
        if (string.IsNullOrEmpty(text))
        {
            errors[field] = Required;
            return null;
        }

        if (text.Length > maxLength)
        {
            errors[field] = TooLong;
            return null;
        }

        return text;
    }

    /// <summary>A required coded enum value, e.g. <c>MOORISH</c>; absent or empty is <see cref="Required"/>.</summary>
    public static TEnum? RequiredCode<TEnum>(string? code, string field, IDictionary<string, string> errors)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrEmpty(code))
        {
            errors[field] = Required;
            return null;
        }

        return OptionalCode<TEnum>(code, field, errors);
    }

    /// <summary>An optional coded enum value; null when absent, reported when unknown.</summary>
    public static TEnum? OptionalCode<TEnum>(string? code, string field, IDictionary<string, string> errors)
        where TEnum : struct, Enum
    {
        if (code is null)
        {
            return null;
        }

        var value = EnumCodes.FromCode<TEnum>(code);
        if (value is null)
        {
            errors[field] = Invalid;
        }

        return value;
    }

    /// <summary>
    /// Letters, marks, numbers, punctuation, symbols and plain spaces only: no control, format
    /// (zero-width, bidi), private-use, unassigned or lone surrogate characters, and no line breaks.
    /// </summary>
    private static bool IsPrintable(string value)
    {
        // EnumerateRunes would silently turn a lone surrogate into U+FFFD, so check them first.
        if (HasLoneSurrogate(value))
        {
            return false;
        }

        foreach (var rune in value.EnumerateRunes())
        {
            switch (Rune.GetUnicodeCategory(rune))
            {
                case UnicodeCategory.Control:
                case UnicodeCategory.Format:
                case UnicodeCategory.PrivateUse:
                case UnicodeCategory.OtherNotAssigned:
                case UnicodeCategory.LineSeparator:
                case UnicodeCategory.ParagraphSeparator:
                    return false;
            }
        }

        return true;
    }

    private static bool HasLoneSurrogate(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (char.IsHighSurrogate(value[i]) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                i++;
            }
            else if (char.IsSurrogate(value[i]))
            {
                return true;
            }
        }

        return false;
    }
}
