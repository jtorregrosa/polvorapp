using PolvorApp.Badges.Contracts;
using PolvorApp.FederationCatalog.Contracts;

namespace PolvorApp.Badges.Documents;

/// <summary>
/// The words a badge sheet prints in each language (spec: Badge language; design D5). Names, comparsas
/// and identifiers are never translated. The labels follow the badge's field order.
/// </summary>
internal sealed record BadgeTexts
{
    public required string Title { get; init; }

    public required string HeaderWord { get; init; }

    /// <summary>Which form of the Federation's official name the sheet prints (add-federation-settings).</summary>
    public required FederationNameForm NameForm { get; init; }

    /// <summary>Surnames, name, DNI/NIE, code, expiry date and comparsa.</summary>
    public required IReadOnlyList<string> Labels { get; init; }

    public static readonly BadgeTexts Spanish = new()
    {
        Title = "Carnets de arcabucero",
        HeaderWord = "ARCABUCERO",
        NameForm = FederationNameForm.Spanish,
        Labels = ["Apellidos", "Nombre", "DNI/NIE", "Código", "Fecha de caducidad", "Comparsa"],
    };

    public static readonly BadgeTexts Valencian = new()
    {
        Title = "Carnets d'arcabusser",
        HeaderWord = "ARCABUSSER",
        NameForm = FederationNameForm.Valencian,
        Labels = ["Cognoms", "Nom", "DNI/NIE", "Codi", "Data de caducitat", "Comparsa"],
    };

    /// <summary>The Federation's name is a proper name: English prints the Spanish form.</summary>
    public static readonly BadgeTexts English = new()
    {
        Title = "Arquebusier badges",
        HeaderWord = "ARQUEBUSIER",
        NameForm = FederationNameForm.Spanish,
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
