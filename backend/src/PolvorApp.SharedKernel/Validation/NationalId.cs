using System.Globalization;
using PolvorApp.SharedKernel.Validation;

namespace PolvorApp.ArquebusierRegistry.NationalIds;

/// <summary>
/// DNI/NIE validation (BR-01, design D4). The frontend mirrors this rule; both run the shared
/// vectors in <c>contracts/test-vectors/national-ids.json</c>. Only ASCII is accepted, so look-alike
/// letters from other scripts and full-width digits are rejected rather than folded.
/// </summary>
internal static class NationalId
{
    public const string Required = InputFields.Required;
    public const string Invalid = InputFields.Invalid;
    public const string CheckLetter = "checkLetter";

    /// <summary>Longer input is rejected before it is copied: a real value has 9 characters plus a few separators.</summary>
    public const int MaxInputLength = 64;

    private const string Letters = "TRWAGMYFPDXBNJZSQVHLCKE";
    private const string NiePrefixes = "XYZ";
    private const int DigitCount = 8;

    /// <summary>The outcome: the normalised value, or the reason it was rejected.</summary>
    public readonly record struct Result(string? Value, string? Error);

    /// <summary>
    /// Removes spaces (U+0020), tabs (U+0009) and hyphens (U+002D), upper-cases ASCII letters and
    /// checks the letter. Any other character makes the value invalid.
    /// </summary>
    public static Result Parse(string? input)
    {
        if (input is not null && input.Length > MaxInputLength)
        {
            return new Result(null, Invalid);
        }

        if (!TryNormalise(input ?? string.Empty, out var normalised))
        {
            return new Result(null, Invalid);
        }

        if (normalised.Length == 0)
        {
            return new Result(null, Required);
        }

        if (!TryNumber(normalised, out var number))
        {
            return new Result(null, Invalid);
        }

        return Letters[number % Letters.Length] == normalised[^1]
            ? new Result(normalised, null)
            : new Result(null, CheckLetter);
    }

    /// <summary>False when a character other than an ASCII letter, digit, space, tab or hyphen appears.</summary>
    private static bool TryNormalise(string input, out string normalised)
    {
        Span<char> buffer = stackalloc char[MaxInputLength];
        var length = 0;
        foreach (var c in input)
        {
            if (c is ' ' or '\t' or '-')
            {
                continue;
            }

            if (!char.IsAsciiLetterOrDigit(c))
            {
                normalised = string.Empty;
                return false;
            }

            buffer[length++] = char.ToUpperInvariant(c);
        }

        normalised = new string(buffer[..length]);
        return true;
    }

    /// <summary>The number the check letter is computed from: the DNI digits, or the NIE with X/Y/Z as 0/1/2.</summary>
    private static bool TryNumber(string value, out int number)
    {
        number = 0;
        if (value.Length != DigitCount + 1 || !char.IsAsciiLetterUpper(value[^1]))
        {
            return false;
        }

        var body = value[..^1];
        var prefix = NiePrefixes.IndexOf(body[0], StringComparison.Ordinal);
        if (prefix >= 0)
        {
            body = (char)('0' + prefix) + body[1..];
        }

        return body.All(char.IsAsciiDigit) && int.TryParse(body, NumberStyles.None, CultureInfo.InvariantCulture, out number);
    }
}
