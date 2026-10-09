using System.Globalization;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.Distribution.Contracts;
using PolvorApp.Exports.Contracts;
using PolvorApp.FederationCatalog.Contracts;

namespace PolvorApp.Distribution.Documents;

/// <summary>
/// The words of the distribution lists and the pickup authorisation form in one language (design D6),
/// chosen by the request's language (maintainer decision). Comparsa names and weapon model labels are
/// written as the catalogue stores them; dates as <c>dd/MM/yyyy</c>.
/// </summary>
internal sealed record DistributionTexts
{
    public required CultureInfo Culture { get; init; }

    // Lists. Titles: {0} is the edition year.
    public required string PowderListTitle { get; init; }

    public required string WeaponsListTitle { get; init; }

    /// <summary>{0} is the date, {1} the location.</summary>
    public required string DayLine { get; init; }

    public required string NumberingNotice { get; init; }

    /// <summary>{0} is the document name, {1} its version.</summary>
    public required string VersionLine { get; init; }

    public required string Number { get; init; }

    public required string Slot { get; init; }

    public required string Comparsa { get; init; }

    public required string Name { get; init; }

    public required string NationalId { get; init; }

    public required string PowderKg { get; init; }

    public required string Flask { get; init; }

    public required string FlaskNumber { get; init; }

    public required string Traceability1 { get; init; }

    public required string Traceability2 { get; init; }

    public required string WeaponModel { get; init; }

    public required string WeaponNumber { get; init; }

    public required string Proxy { get; init; }

    public required string ProxyNationalId { get; init; }

    /// <summary>The powder list's column saying who collected a recorded handover (UC-21).</summary>
    public required string CollectedBy { get; init; }

    public required string CollectedByHolder { get; init; }

    public required string CollectedByProxy { get; init; }

    /// <summary>{0} is the number of handovers recorded for the powder day.</summary>
    public required string HandoversLine { get; init; }

    public required IReadOnlyDictionary<FlaskOption, string> Flasks { get; init; }

    /// <summary>Printed instead of a name erased on a GDPR request (UC-26): never a blank that looks valid.</summary>
    public required string ErasedPerson { get; init; }

    // Form.
    /// <summary>Which form of the Federation's official name the form prints (add-federation-settings).</summary>
    public required FederationNameForm NameForm { get; init; }

    /// <summary>{0} is the edition year.</summary>
    public required IReadOnlyDictionary<DistributionType, string> FormTitles { get; init; }

    public required string HolderHeading { get; init; }

    public required string ProxyHeading { get; init; }

    public required string FormName { get; init; }

    public required string FormNationalId { get; init; }

    public required string FormLicense { get; init; }

    public required string FormComparsa { get; init; }

    /// <summary>What the holder cannot collect, for the statement.</summary>
    public required IReadOnlyDictionary<DistributionType, string> Collects { get; init; }

    /// <summary>{0} is what is collected; {1} the day ("" when not planned).</summary>
    public required string HolderStatement { get; init; }

    /// <summary>{0} is the date, {1} the location.</summary>
    public required string PlannedDay { get; init; }

    public required string Reason { get; init; }

    /// <summary>{0} is the edition year.</summary>
    public required string AuthorisationStatement { get; init; }

    public required string SameComparsa { get; init; }

    public required string PlaceAndDate { get; init; }

    public required string HolderSignature { get; init; }

    public required string ProxySignature { get; init; }

    public static DistributionTexts Spanish { get; } = new()
    {
        Culture = CultureInfo.GetCultureInfo("es-ES"),
        PowderListTitle = "Reparto de pólvora — Fiestas {0}",
        WeaponsListTitle = "Reparto de armas de alquiler — Fiestas {0}",
        DayLine = "Día: {0} · Lugar: {1}",
        NumberingNotice = "Numeración válida para esta impresión: si los datos cambian, otra impresión puede numerar distinto.",
        VersionLine = "{0}, versión {1}",
        Number = "Nº",
        Slot = "Turno",
        Comparsa = "Comparsa",
        Name = "Apellidos y nombre",
        NationalId = "DNI/NIE",
        PowderKg = "Kg",
        Flask = "Cantimplora",
        FlaskNumber = "Nº cantimplora",
        Traceability1 = "Trazabilidad 1",
        Traceability2 = "Trazabilidad 2",
        WeaponModel = "Modelo",
        WeaponNumber = "Nº de arma",
        Proxy = "Autorizado",
        ProxyNationalId = "DNI/NIE autorizado",
        CollectedBy = "Recogida por",
        CollectedByHolder = "Titular",
        CollectedByProxy = "Autorizado",
        HandoversLine = "Entregas registradas: {0}",
        Flasks = FlaskWords("Propia", "Alquiler 1 kg", "Alquiler 2 kg", "Ninguna"),
        ErasedPerson = "[datos borrados]",
        NameForm = FederationNameForm.Spanish,
        FormTitles = new Dictionary<DistributionType, string>
        {
            [DistributionType.Powder] = "Autorización de recogida de pólvora — Fiestas {0}",
            [DistributionType.Weapons] = "Autorización de recogida del arma de alquiler — Fiestas {0}",
        },
        HolderHeading = "Titular",
        ProxyHeading = "Autorizado",
        FormName = "Apellidos y nombre:",
        FormNationalId = "DNI/NIE:",
        FormLicense = "Licencia de armas:",
        FormComparsa = "Comparsa:",
        Collects = new Dictionary<DistributionType, string>
        {
            [DistributionType.Powder] = "la pólvora que me corresponde",
            [DistributionType.Weapons] = "el arma de alquiler que me corresponde",
        },
        HolderStatement = "Ante la imposibilidad de recoger {0} el día del reparto{1}, por el motivo que indico:",
        PlannedDay = " ({0}, {1})",
        Reason = "Motivo:",
        AuthorisationStatement = "AUTORIZO a recogerla en mi nombre, en las Fiestas {0}, a:",
        SameComparsa = "Perteneciente a la misma comparsa.",
        PlaceAndDate = "Lugar y fecha:",
        HolderSignature = "Firma del titular",
        ProxySignature = "Firma del autorizado",
    };

    public static DistributionTexts Valencian { get; } = new()
    {
        Culture = CultureInfo.GetCultureInfo("ca-ES-valencia"),
        PowderListTitle = "Repartiment de pólvora — Festes {0}",
        WeaponsListTitle = "Repartiment d'armes de lloguer — Festes {0}",
        DayLine = "Dia: {0} · Lloc: {1}",
        NumberingNotice = "Numeració vàlida per a esta impressió: si les dades canvien, una altra impressió pot numerar diferent.",
        VersionLine = "{0}, versió {1}",
        Number = "Núm.",
        Slot = "Torn",
        Comparsa = "Comparsa",
        Name = "Cognoms i nom",
        NationalId = "DNI/NIE",
        PowderKg = "Kg",
        Flask = "Cantimplora",
        FlaskNumber = "Núm. cantimplora",
        Traceability1 = "Traçabilitat 1",
        Traceability2 = "Traçabilitat 2",
        WeaponModel = "Model",
        WeaponNumber = "Núm. d'arma",
        Proxy = "Autoritzat",
        ProxyNationalId = "DNI/NIE autoritzat",
        CollectedBy = "Recollida per",
        CollectedByHolder = "Titular",
        CollectedByProxy = "Autoritzat",
        HandoversLine = "Entregues registrades: {0}",
        Flasks = FlaskWords("Pròpia", "Lloguer 1 kg", "Lloguer 2 kg", "Cap"),
        ErasedPerson = "[dades esborrades]",
        NameForm = FederationNameForm.Valencian,
        FormTitles = new Dictionary<DistributionType, string>
        {
            [DistributionType.Powder] = "Autorització de recollida de pólvora — Festes {0}",
            [DistributionType.Weapons] = "Autorització de recollida de l'arma de lloguer — Festes {0}",
        },
        HolderHeading = "Titular",
        ProxyHeading = "Autoritzat",
        FormName = "Cognoms i nom:",
        FormNationalId = "DNI/NIE:",
        FormLicense = "Llicència d'armes:",
        FormComparsa = "Comparsa:",
        Collects = new Dictionary<DistributionType, string>
        {
            [DistributionType.Powder] = "la pólvora que em correspon",
            [DistributionType.Weapons] = "l'arma de lloguer que em correspon",
        },
        HolderStatement = "Davant la impossibilitat de recollir {0} el dia del repartiment{1}, pel motiu que indique:",
        PlannedDay = " ({0}, {1})",
        Reason = "Motiu:",
        AuthorisationStatement = "AUTORITZE a recollir-la en el meu nom, en les Festes {0}, a:",
        SameComparsa = "Pertanyent a la mateixa comparsa.",
        PlaceAndDate = "Lloc i data:",
        HolderSignature = "Signatura del titular",
        ProxySignature = "Signatura de l'autoritzat",
    };

    public static DistributionTexts English { get; } = new()
    {
        Culture = CultureInfo.GetCultureInfo("en"),
        PowderListTitle = "Powder distribution — Festival {0}",
        WeaponsListTitle = "Rented weapons distribution — Festival {0}",
        DayLine = "Day: {0} · Place: {1}",
        NumberingNotice = "Numbering valid for this print: if the data changes, another print may number differently.",
        VersionLine = "{0}, version {1}",
        Number = "No.",
        Slot = "Slot",
        Comparsa = "Comparsa",
        Name = "Last name, first name",
        NationalId = "DNI/NIE",
        PowderKg = "Kg",
        Flask = "Flask",
        FlaskNumber = "Flask no.",
        Traceability1 = "Traceability 1",
        Traceability2 = "Traceability 2",
        WeaponModel = "Model",
        WeaponNumber = "Weapon no.",
        Proxy = "Proxy",
        ProxyNationalId = "Proxy DNI/NIE",
        CollectedBy = "Collected by",
        CollectedByHolder = "Holder",
        CollectedByProxy = "Proxy",
        HandoversLine = "Handovers recorded: {0}",
        Flasks = FlaskWords("Own", "Rented 1 kg", "Rented 2 kg", "None"),
        ErasedPerson = "[data erased]",
        NameForm = FederationNameForm.Spanish,
        FormTitles = new Dictionary<DistributionType, string>
        {
            [DistributionType.Powder] = "Authorisation to collect the powder — Festival {0}",
            [DistributionType.Weapons] = "Authorisation to collect the rented weapon — Festival {0}",
        },
        HolderHeading = "Holder",
        ProxyHeading = "Proxy",
        FormName = "Last name, first name:",
        FormNationalId = "DNI/NIE:",
        FormLicense = "Weapons license:",
        FormComparsa = "Comparsa:",
        Collects = new Dictionary<DistributionType, string>
        {
            [DistributionType.Powder] = "my powder",
            [DistributionType.Weapons] = "my rented weapon",
        },
        HolderStatement = "As I am unable to collect {0} on distribution day{1}, for the reason given:",
        PlannedDay = " ({0}, {1})",
        Reason = "Reason:",
        AuthorisationStatement = "I AUTHORISE the following person to collect it on my behalf, for the Festival {0}:",
        SameComparsa = "Member of the same comparsa.",
        PlaceAndDate = "Place and date:",
        HolderSignature = "Holder's signature",
        ProxySignature = "Proxy's signature",
    };

    public static DistributionTexts For(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return culture.TwoLetterISOLanguageName switch
        {
            "ca" => Valencian,
            "en" => English,
            _ => Spanish,
        };
    }

    public string Format(string format, params object?[] values) => string.Format(Culture, format, values);

    public static string Date(DateOnly date) => date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    private static Dictionary<FlaskOption, string> FlaskWords(string owned, string oneKg, string twoKg, string none) => new()
    {
        [FlaskOption.Owned] = owned,
        [FlaskOption.Rental1Kg] = oneKg,
        [FlaskOption.Rental2Kg] = twoKg,
        [FlaskOption.None] = none,
    };
}
