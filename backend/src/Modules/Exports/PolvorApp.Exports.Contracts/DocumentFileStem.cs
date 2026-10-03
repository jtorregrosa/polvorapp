using System.Text.RegularExpressions;

namespace PolvorApp.Exports.Contracts;

/// <summary>
/// The rule for a document's file name without extension (spec: file names contain no personal data):
/// lower-case ASCII words joined by single hyphens, as <c>SharedKernel.Text.FileSlug</c> builds them,
/// at most 120 characters. It keeps paths, quotes, line breaks and accented names out of the
/// download's <c>Content-Disposition</c>; builders still never put a person's name in it.
/// </summary>
public static partial class DocumentFileStem
{
    public const int MaxLength = 120;

    /// <summary>Whether <paramref name="stem"/> follows the rule.</summary>
    public static bool IsValid(string? stem) => stem is { Length: > 0 and <= MaxLength } && Slug().IsMatch(stem);

    /// <summary>Throws when <paramref name="stem"/> breaks the rule; the message never repeats the value.</summary>
    public static void Check(string? stem, string paramName)
    {
        if (!IsValid(stem))
        {
            throw new ArgumentException(
                $"A document file stem is lower-case ASCII words joined by single hyphens, at most {MaxLength} characters.", paramName);
        }
    }

    // \z, not $: "$" would also accept a stem ending in a line break.
    [GeneratedRegex(@"^[a-z0-9]+(-[a-z0-9]+)*\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex Slug();
}
