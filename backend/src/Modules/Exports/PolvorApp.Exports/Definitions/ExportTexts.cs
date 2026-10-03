using System.Globalization;
using PolvorApp.ComparsaOrders.Contracts;

namespace PolvorApp.Exports.Definitions;

/// <summary>
/// The words of the exports in one language (design D6): recipient exports always use
/// <see cref="Spanish"/>; the comparsa list follows the user's language. Values from the catalogue
/// (comparsa names, weapon model labels) are written as stored.
/// </summary>
internal sealed record ExportTexts
{
    public required CultureInfo Culture { get; init; }

    // Titles: {0} is the edition year; for the comparsa list {1} is the comparsa.
    public required string PowderSupplierTitle { get; init; }

    public required string RentalCompanyTitle { get; init; }

    public required string ArmsAuthorityTitle { get; init; }

    public required string ComparsaListTitle { get; init; }

    public required string ProvisionalNotice { get; init; }

    /// <summary>{0} is the order's status in words.</summary>
    public required string DraftNotice { get; init; }

    /// <summary>{0} is the definition name, {1} its version.</summary>
    public required string VersionLine { get; init; }

    public required string Total { get; init; }

    // Headings.
    public required string Comparsa { get; init; }

    public required string PowderKg { get; init; }

    public required string NormalCapsBoxes { get; init; }

    public required string SmallCapsBoxes { get; init; }

    public required string Name { get; init; }

    public required string NationalId { get; init; }

    public required string FederationId { get; init; }

    public required string Status { get; init; }

    public required string CapsBoxes { get; init; }

    public required string CapsKind { get; init; }

    public required string Weapon { get; init; }

    public required string WeaponModel { get; init; }

    public required string WeaponNumber { get; init; }

    public required string OwnershipGuide { get; init; }

    public required string Source { get; init; }

    public required string Lender { get; init; }

    public required string LenderNationalId { get; init; }

    public required string License { get; init; }

    public required string LicenseExpiresOn { get; init; }

    public required string Flask { get; init; }

    // Words.
    public required IReadOnlyDictionary<OrderStatus, string> OrderStatuses { get; init; }

    public required string Active { get; init; }

    public required string Reserve { get; init; }

    public required IReadOnlyDictionary<WeaponSource, string> WeaponSources { get; init; }

    /// <summary>The flask words; the rented sizes are the same in every language ("1 kg", "2 kg").</summary>
    public required IReadOnlyDictionary<FlaskOption, string> Flasks { get; init; }

    public required IReadOnlyDictionary<CapsType, string> CapsTypes { get; init; }

    /// <summary>{0} is the lender's name: the loan part of the weapon column of the comparsa list.</summary>
    public required string LentBy { get; init; }

    /// <summary>The license cell of an arquebusier in the registry without a license.</summary>
    public required string NoLicense { get; init; }

    /// <summary>A weapon whose details are not filled in yet (a draft's loan without its owner).</summary>
    public required string NoDetails { get; init; }

    public static ExportTexts Spanish { get; } = new()
    {
        Culture = CultureInfo.GetCultureInfo("es-ES"),
        PowderSupplierTitle = "Pedido de pólvora y pistones · Fiestas {0}",
        RentalCompanyTitle = "Alquiler de armas y cantimploras · Fiestas {0}",
        ArmsAuthorityTitle = "Relación de arcabuceros y armas · Fiestas {0}",
        ComparsaListTitle = "Pedido de {1} · Fiestas {0}",
        ProvisionalNotice = "PROVISIONAL: formato pendiente de la plantilla del destinatario. No lo envíes como definitivo.",
        DraftNotice = "BORRADOR: el pedido está {0} y aún no está validado; puede cambiar.",
        VersionLine = "Definición {0}, versión {1}",
        Total = "Total",
        Comparsa = "Comparsa",
        PowderKg = "Pólvora (kg)",
        NormalCapsBoxes = "Cajas de pistones normales",
        SmallCapsBoxes = "Cajas de pistones pequeños",
        Name = "Apellidos y nombre",
        NationalId = "DNI/NIE",
        FederationId = "ID Unión",
        Status = "Estado",
        CapsBoxes = "Cajas de pistones",
        CapsKind = "Tipo de pistones",
        Weapon = "Arma",
        WeaponModel = "Modelo de arma",
        WeaponNumber = "Número de arma",
        OwnershipGuide = "Guía de pertenencia",
        Source = "Procedencia",
        Lender = "Cedente",
        LenderNationalId = "DNI/NIE del cedente",
        License = "Licencia",
        LicenseExpiresOn = "Caducidad",
        Flask = "Cantimplora",
        OrderStatuses = new Dictionary<OrderStatus, string>
        {
            [OrderStatus.Draft] = "en borrador",
            [OrderStatus.Submitted] = "enviado",
            [OrderStatus.Returned] = "devuelto",
            [OrderStatus.Validated] = "validado",
        },
        Active = "Activo",
        Reserve = "Reserva",
        WeaponSources = new Dictionary<WeaponSource, string>
        {
            [WeaponSource.Owned] = "Propia",
            [WeaponSource.Rental] = "Alquiler",
            [WeaponSource.Loan] = "Cesión",
            [WeaponSource.None] = "Sin arma",
        },
        Flasks = FlaskWords("Propia", "Sin cantimplora"),
        CapsTypes = new Dictionary<CapsType, string>
        {
            [CapsType.Normal] = "Normales",
            [CapsType.Small] = "Pequeños",
        },
        LentBy = "cedida por {0}",
        NoLicense = "Sin licencia",
        NoDetails = "sin datos",
    };

    public static ExportTexts Valencian { get; } = Spanish with
    {
        Culture = CultureInfo.GetCultureInfo("ca-ES-valencia"),
        PowderSupplierTitle = "Comanda de pólvora i pistons · Festes {0}",
        RentalCompanyTitle = "Lloguer d'armes i cantimplores · Festes {0}",
        ArmsAuthorityTitle = "Relació d'arcabussers i armes · Festes {0}",
        ComparsaListTitle = "Comanda de {1} · Festes {0}",
        ProvisionalNotice = "PROVISIONAL: format pendent de la plantilla del destinatari. No l'envies com a definitiu.",
        DraftNotice = "ESBORRANY: la comanda està {0} i encara no està validada; pot canviar.",
        VersionLine = "Definició {0}, versió {1}",
        PowderKg = "Pólvora (kg)",
        NormalCapsBoxes = "Caixes de pistons normals",
        SmallCapsBoxes = "Caixes de pistons xicotets",
        Name = "Cognoms i nom",
        FederationId = "ID Unió",
        Status = "Estat",
        CapsBoxes = "Caixes de pistons",
        CapsKind = "Tipus de pistons",
        Weapon = "Arma",
        WeaponModel = "Model d'arma",
        WeaponNumber = "Número d'arma",
        OwnershipGuide = "Guia de pertinença",
        Source = "Procedència",
        Lender = "Cedent",
        LenderNationalId = "DNI/NIE del cedent",
        License = "Llicència",
        LicenseExpiresOn = "Caducitat",
        Flask = "Cantimplora",
        OrderStatuses = new Dictionary<OrderStatus, string>
        {
            [OrderStatus.Draft] = "en esborrany",
            [OrderStatus.Submitted] = "enviada",
            [OrderStatus.Returned] = "tornada",
            [OrderStatus.Validated] = "validada",
        },
        Active = "Actiu",
        Reserve = "Reserva",
        WeaponSources = new Dictionary<WeaponSource, string>
        {
            [WeaponSource.Owned] = "Pròpia",
            [WeaponSource.Rental] = "Lloguer",
            [WeaponSource.Loan] = "Cessió",
            [WeaponSource.None] = "Sense arma",
        },
        Flasks = FlaskWords("Pròpia", "Sense cantimplora"),
        CapsTypes = new Dictionary<CapsType, string>
        {
            [CapsType.Normal] = "Normals",
            [CapsType.Small] = "Xicotets",
        },
        LentBy = "cedida per {0}",
        NoLicense = "Sense llicència",
        NoDetails = "sense dades",
    };

    public static ExportTexts English { get; } = Spanish with
    {
        Culture = CultureInfo.GetCultureInfo("en-GB"),
        PowderSupplierTitle = "Powder and caps order · Festival {0}",
        RentalCompanyTitle = "Weapon and flask rentals · Festival {0}",
        ArmsAuthorityTitle = "Arquebusiers and weapons · Festival {0}",
        ComparsaListTitle = "Order of {1} · Festival {0}",
        ProvisionalNotice = "PROVISIONAL: format pending the recipient's template. Do not send it as final.",
        DraftNotice = "DRAFT: the order is {0} and not validated yet; it may change.",
        VersionLine = "Definition {0}, version {1}",
        Total = "Total",
        PowderKg = "Powder (kg)",
        NormalCapsBoxes = "Normal caps boxes",
        SmallCapsBoxes = "Small caps boxes",
        Name = "Surnames and name",
        NationalId = "DNI/NIE",
        FederationId = "Federation ID",
        Status = "Status",
        CapsBoxes = "Caps boxes",
        CapsKind = "Caps type",
        Weapon = "Weapon",
        WeaponModel = "Weapon model",
        WeaponNumber = "Weapon number",
        OwnershipGuide = "Ownership guide",
        Source = "Source",
        Lender = "Lender",
        LenderNationalId = "Lender's DNI/NIE",
        License = "Licence",
        LicenseExpiresOn = "Expiry",
        Flask = "Flask",
        OrderStatuses = new Dictionary<OrderStatus, string>
        {
            [OrderStatus.Draft] = "a draft",
            [OrderStatus.Submitted] = "submitted",
            [OrderStatus.Returned] = "returned",
            [OrderStatus.Validated] = "validated",
        },
        Active = "Active",
        Reserve = "Reserve",
        WeaponSources = new Dictionary<WeaponSource, string>
        {
            [WeaponSource.Owned] = "Owned",
            [WeaponSource.Rental] = "Rental",
            [WeaponSource.Loan] = "Loan",
            [WeaponSource.None] = "No weapon",
        },
        Flasks = FlaskWords("Owned", "No flask"),
        CapsTypes = new Dictionary<CapsType, string>
        {
            [CapsType.Normal] = "Normal",
            [CapsType.Small] = "Small",
        },
        LentBy = "lent by {0}",
        NoLicense = "No licence",
        NoDetails = "no details",
    };

    /// <summary>The texts of a UI culture (the comparsa list), by its language; Spanish otherwise.</summary>
    public static ExportTexts For(CultureInfo culture)
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

    private static Dictionary<FlaskOption, string> FlaskWords(string owned, string none) => new()
    {
        [FlaskOption.Rental1Kg] = "1 kg",
        [FlaskOption.Rental2Kg] = "2 kg",
        [FlaskOption.Owned] = owned,
        [FlaskOption.None] = none,
    };
}
