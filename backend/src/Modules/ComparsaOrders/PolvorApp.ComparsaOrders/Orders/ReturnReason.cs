using System.Globalization;
using System.Text;
using PolvorApp.SharedKernel.Validation;

namespace PolvorApp.ComparsaOrders.Orders;

/// <summary>
/// The reason an Admin returns an order with (spec: Reviewing orders (UC-15)): 1 to 500 characters
/// after trimming, in NFC. Unlike single-line fields it may span several lines; any other control,
/// format or separator character is invalid.
/// </summary>
internal static class ReturnReason
{
    public const string Field = "returnReason";

    public static string? Read(string? value, IDictionary<string, string> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        if (value is not null && value.Length > ComparsaOrder.ReturnReasonMaxLength * InputFields.MaxRawLengthFactor)
        {
            errors[Field] = InputFields.TooLong;
            return null;
        }

        // Before normalising, which would silently replace a lone surrogate.
        if (value is not null && HasLoneSurrogate(value))
        {
            errors[Field] = InputFields.Invalid;
            return null;
        }

        var text = value?.Replace("\r\n", "\n", StringComparison.Ordinal).Normalize(NormalizationForm.FormC).Trim();
        if (string.IsNullOrEmpty(text))
        {
            errors[Field] = InputFields.Required;
            return null;
        }

        if (!IsMultilineText(text))
        {
            errors[Field] = InputFields.Invalid;
            return null;
        }

        if (text.Length > ComparsaOrder.ReturnReasonMaxLength)
        {
            errors[Field] = InputFields.TooLong;
            return null;
        }

        return text;
    }

    private static bool IsMultilineText(string text)
    {
        foreach (var c in text)
        {
            if (char.IsSurrogate(c))
            {
                continue;
            }

            switch (char.GetUnicodeCategory(c))
            {
                case UnicodeCategory.Control when c != '\n':
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
