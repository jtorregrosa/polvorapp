using PolvorApp.Badges.Contracts;
using PolvorApp.Exports.Contracts;

namespace PolvorApp.Badges.Documents;

/// <summary>
/// The words a badge sheet prints in each language (spec: Badge language; design D5). Names, comparsas
/// and identifiers are never translated. The labels follow the badge's field order.
/// </summary>
internal sealed record BadgeTexts
{
    public required string Title { get; init; }

    public required string HeaderWord { get; init; }

    public required string FederationName { get; init; }

    /// <summary>Surnames, name, DNI/NIE, code, expiry date and comparsa.</summary>
    public required IReadOnlyList<string> Labels { get; init; }

    public static readonly BadgeTexts Spanish = new()
    {
        Title = "Carnets de arcabucero",
        HeaderWord = "ARCABUCERO",
        FederationName = FederationNames.Spanish,
        Labels = ["Apellidos", "Nombre", "DNI/NIE", "Código", "Fecha de caducidad", "Comparsa"],
    };

    public static readonly BadgeTexts Valencian = new()
    {
        Title = "Carnets d'arcabusser",
        HeaderWord = "ARCABUSSER",
        FederationName = FederationNames.Valencian,
        Labels = ["Cognoms", "Nom", "DNI/NIE", "Codi", "Data de caducitat", "Comparsa"],
    };

    /// <summary>The Federation's name is a proper name: English uses the Spanish form.</summary>
    public static readonly BadgeTexts English = new()
    {
        Title = "Arquebusier badges",
        HeaderWord = "ARQUEBUSIER",
        FederationName = FederationNames.Spanish,
        Labels = ["Surnames", "Name", "DNI/NIE", "Code", "Expiry date", "Comparsa"],
    };

    public static BadgeTexts For(BadgeLanguage language) => language switch
    {
        BadgeLanguage.Spanish => Spanish,
        BadgeLanguage.Valencian => Valencian,
        BadgeLanguage.English => English,
        _ => throw new ArgumentOutOfRangeException(nameof(language), language, "Unknown badge language."),
    };
}
