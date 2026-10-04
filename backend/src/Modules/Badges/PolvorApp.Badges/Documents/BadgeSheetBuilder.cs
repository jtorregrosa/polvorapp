using System.Globalization;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.Badges.Batches;
using PolvorApp.Badges.Contracts;
using PolvorApp.Exports.Contracts;
using PolvorApp.SharedKernel.Text;

namespace PolvorApp.Badges.Documents;

/// <summary>One badge's person, as the registry holds them when the sheet is generated.</summary>
/// <param name="License">The current license, or null; only an issued one prints its expiry.</param>
/// <param name="Photo">The ID photo scaled for print, or null for an empty frame.</param>
internal sealed record BadgePerson(
    string LastName, string FirstName, string NationalId, int FederationId, string ComparsaName, ArquebusierLicenseFacts? License, DocumentImage? Photo)
{
    /// <summary>The type name only: the members are personal data.</summary>
    public override string ToString() => nameof(BadgePerson);
}

/// <summary>What a badge sheet is built from: the batch, the language, the date, the people in print order and the logo.</summary>
/// <param name="ComparsaName">The comparsa of a comparsa batch, for the file name; ignored for a selection.</param>
internal sealed record BadgeSheetContent(
    BadgeBatchKind Kind, string? ComparsaName, BadgeLanguage Language, DateOnly GeneratedOn, IReadOnlyList<BadgePerson> People, DocumentImage? Logo);

/// <summary>
/// Builds the badge sheet (spec: Badge content, Incomplete badges are warnings; design D4, D5): the
/// words of the chosen language and, per person, surnames, name, DNI/NIE, federationId, the expiry of an
/// issued license as <c>dd/MM/yyyy</c> (an empty line otherwise) and the comparsa. Nothing else of the
/// person is printed. The file name holds the comparsa's slug or the selection's count, and the date.
/// </summary>
internal static class BadgeSheetBuilder
{
    /// <summary>The badge layout's version, recorded with each download.</summary>
    public const string Version = "1";

    private const int MaxComparsaSlug = 40;

    public static DocumentBadgeSheet Build(BadgeSheetContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var texts = BadgeTexts.For(content.Language);
        return new DocumentBadgeSheet(
            FileStem(content),
            texts.Title,
            texts.HeaderWord,
            texts.FederationName,
            texts.Labels,
            [.. content.People.Select(Badge)],
            content.Logo);
    }

    private static DocumentBadge Badge(BadgePerson person) => new(
        person.Photo,
        [
            person.LastName,
            person.FirstName,
            person.NationalId,
            person.FederationId.ToString(CultureInfo.InvariantCulture),
            person.License is ArquebusierLicenseFacts.Issued issued ? issued.ExpiresOn.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) : null,
            person.ComparsaName,
        ]);

    private static string FileStem(BadgeSheetContent content)
    {
        var date = content.GeneratedOn.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        if (content.Kind == BadgeBatchKind.Selection)
        {
            return string.Join('-', "polvorapp-badges-selection", content.People.Count.ToString(CultureInfo.InvariantCulture), date);
        }

        var slug = FileSlug.Of(content.ComparsaName ?? string.Empty);
        var comparsa = slug.Length > MaxComparsaSlug ? slug[..MaxComparsaSlug].TrimEnd('-') : slug;
        return string.Join('-', "polvorapp-badges", comparsa.Length > 0 ? comparsa : "comparsa", date);
    }
}
