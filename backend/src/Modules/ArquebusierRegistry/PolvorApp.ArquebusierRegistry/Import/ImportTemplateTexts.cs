using System.Globalization;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.SharedKernel.Localization;

namespace PolvorApp.ArquebusierRegistry.Import;

/// <summary>
/// The texts of the import template in one language (spec: Import template; design D8). The reader
/// recognises headers and values in every language from these same texts, so the template and the
/// reader cannot drift. A record per language, rather than resource files, lets the compiler check
/// that no language misses a text.
/// </summary>
internal sealed record ImportTemplateTexts(
    string Culture,
    string DataSheet,
    string InstructionsSheet,
    string ListsSheet,
    IReadOnlyDictionary<ImportColumn, string> Headers,
    IReadOnlyDictionary<ImportColumn, string> Descriptions,
    IReadOnlyDictionary<Gender, string> Genders,
    IReadOnlyDictionary<ArquebusierStatus, string> Statuses,
    string ColumnHeading,
    string RequiredHeading,
    string DescriptionHeading,
    string Yes,
    string No,
    IReadOnlyList<string> Notes)
{
    public static readonly ImportTemplateTexts Spanish = new(
        SupportedLocales.Spanish,
        DataSheet: "Arcabuceros",
        InstructionsSheet: "Instrucciones",
        ListsSheet: "Listas",
        Headers: new Dictionary<ImportColumn, string>
        {
            [ImportColumn.FederationId] = "ID Unión",
            [ImportColumn.LastName] = "Apellidos",
            [ImportColumn.FirstName] = "Nombre",
            [ImportColumn.NationalId] = "DNI/NIE",
            [ImportColumn.BirthDate] = "Fecha de nacimiento",
            [ImportColumn.Gender] = "Género",
            [ImportColumn.Email] = "Correo electrónico",
            [ImportColumn.Phone] = "Teléfono",
            [ImportColumn.Status] = "Estado",
            [ImportColumn.LicenseType] = "Tipo de licencia",
            [ImportColumn.LicenseIssuedOn] = "Fecha de expedición",
            [ImportColumn.LicenseExpiresOn] = "Fecha de caducidad",
            [ImportColumn.TrainingCompletedOn] = "Fecha del curso",
        },
        Descriptions: new Dictionary<ImportColumn, string>
        {
            [ImportColumn.FederationId] = "Código del arcabucero en la aplicación de la Federación (de 1 a 999999999). No se puede repetir.",
            [ImportColumn.LastName] = "Hasta 100 caracteres.",
            [ImportColumn.FirstName] = "Hasta 100 caracteres.",
            [ImportColumn.NationalId] = "DNI: 8 dígitos y la letra; si faltan ceros a la izquierda, se añaden. NIE: X, Y o Z, 7 dígitos y la letra. No se puede repetir.",
            [ImportColumn.BirthDate] = "Fecha (dd/mm/aaaa).",
            [ImportColumn.Gender] = "Hombre, Mujer o Sin especificar. Solo para los informes de igualdad.",
            [ImportColumn.Email] = "Una dirección de correo electrónico.",
            [ImportColumn.Phone] = "Dígitos y espacios, con un + inicial opcional; hasta 20 caracteres.",
            [ImportColumn.Status] = "Activo o Reserva. Vacío es Activo.",
            [ImportColumn.LicenseType] = "AE o A-PROF. Vacío si no tiene licencia. Sin fechas, la licencia queda en trámite.",
            [ImportColumn.LicenseIssuedOn] = "Fecha (dd/mm/aaaa), solo con tipo de licencia. No puede ser futura.",
            [ImportColumn.LicenseExpiresOn] = "Fecha (dd/mm/aaaa), solo con fecha de expedición. Vacía: 5 años después para AE y 1 año para A-PROF.",
            [ImportColumn.TrainingCompletedOn] = "Fecha (dd/mm/aaaa) en que hizo el curso de arcabucería. Vacía si no lo ha hecho.",
        },
        Genders: new Dictionary<Gender, string>
        {
            [Gender.Male] = "Hombre",
            [Gender.Female] = "Mujer",
            [Gender.Unspecified] = "Sin especificar",
        },
        Statuses: new Dictionary<ArquebusierStatus, string>
        {
            [ArquebusierStatus.Active] = "Activo",
            [ArquebusierStatus.Reserve] = "Reserva",
        },
        ColumnHeading: "Columna",
        RequiredHeading: "Obligatoria",
        DescriptionHeading: "Qué escribir",
        Yes: "Sí",
        No: "No",
        Notes:
        [
            "Un fichero por comparsa: la comparsa se elige al importar.",
            "Rellena la hoja «Arcabuceros» desde la fila 2, sin cambiar la fila de cabeceras.",
            "Solo ficheros .xlsx de hasta 2 MB y 1000 arcabuceros.",
            "Las fechas, como fecha o como texto dd/mm/aaaa.",
            "Solo se dan de alta arcabuceros nuevos: si un DNI/NIE o un ID Unión ya está registrado, esa fila es un error.",
            "Si alguna fila tiene un error, no se importa nada: corrige el fichero y vuelve a comprobarlo.",
            "Las fotos y las armas propias se añaden después en la ficha de cada arcabucero.",
        ]);

    public static readonly ImportTemplateTexts Valencian = new(
        SupportedLocales.Valencian,
        DataSheet: "Arcabussers",
        InstructionsSheet: "Instruccions",
        ListsSheet: "Llistes",
        Headers: new Dictionary<ImportColumn, string>
        {
            [ImportColumn.FederationId] = "ID Unió",
            [ImportColumn.LastName] = "Cognoms",
            [ImportColumn.FirstName] = "Nom",
            [ImportColumn.NationalId] = "DNI/NIE",
            [ImportColumn.BirthDate] = "Data de naixement",
            [ImportColumn.Gender] = "Gènere",
            [ImportColumn.Email] = "Correu electrònic",
            [ImportColumn.Phone] = "Telèfon",
            [ImportColumn.Status] = "Estat",
            [ImportColumn.LicenseType] = "Tipus de llicència",
            [ImportColumn.LicenseIssuedOn] = "Data d'expedició",
            [ImportColumn.LicenseExpiresOn] = "Data de caducitat",
            [ImportColumn.TrainingCompletedOn] = "Data del curs",
        },
        Descriptions: new Dictionary<ImportColumn, string>
        {
            [ImportColumn.FederationId] = "Codi de l'arcabusser en l'aplicació de la Federació (d'1 a 999999999). No es pot repetir.",
            [ImportColumn.LastName] = "Fins a 100 caràcters.",
            [ImportColumn.FirstName] = "Fins a 100 caràcters.",
            [ImportColumn.NationalId] = "DNI: 8 dígits i la lletra; si falten zeros a l'esquerra, s'afigen. NIE: X, Y o Z, 7 dígits i la lletra. No es pot repetir.",
            [ImportColumn.BirthDate] = "Data (dd/mm/aaaa).",
            [ImportColumn.Gender] = "Home, Dona o Sense especificar. Només per als informes d'igualtat.",
            [ImportColumn.Email] = "Una adreça de correu electrònic.",
            [ImportColumn.Phone] = "Dígits i espais, amb un + inicial opcional; fins a 20 caràcters.",
            [ImportColumn.Status] = "Actiu o Reserva. Buit és Actiu.",
            [ImportColumn.LicenseType] = "AE o A-PROF. Buit si no té llicència. Sense dates, la llicència queda en tràmit.",
            [ImportColumn.LicenseIssuedOn] = "Data (dd/mm/aaaa), només amb tipus de llicència. No pot ser futura.",
            [ImportColumn.LicenseExpiresOn] = "Data (dd/mm/aaaa), només amb data d'expedició. Buida: 5 anys després per a AE i 1 any per a A-PROF.",
            [ImportColumn.TrainingCompletedOn] = "Data (dd/mm/aaaa) en què va fer el curs d'arcabusseria. Buida si no l'ha fet.",
        },
        Genders: new Dictionary<Gender, string>
        {
            [Gender.Male] = "Home",
            [Gender.Female] = "Dona",
            [Gender.Unspecified] = "Sense especificar",
        },
        Statuses: new Dictionary<ArquebusierStatus, string>
        {
            [ArquebusierStatus.Active] = "Actiu",
            [ArquebusierStatus.Reserve] = "Reserva",
        },
        ColumnHeading: "Columna",
        RequiredHeading: "Obligatòria",
        DescriptionHeading: "Què cal escriure",
        Yes: "Sí",
        No: "No",
        Notes:
        [
            "Un fitxer per comparsa: la comparsa es tria en importar.",
            "Omple el full «Arcabussers» des de la fila 2, sense canviar la fila de capçaleres.",
            "Només fitxers .xlsx de fins a 2 MB i 1000 arcabussers.",
            "Les dates, com a data o com a text dd/mm/aaaa.",
            "Només es donen d'alta arcabussers nous: si un DNI/NIE o un ID Unió ja està registrat, eixa fila és un error.",
            "Si alguna fila té un error, no s'importa res: corregix el fitxer i torna a comprovar-lo.",
            "Les fotos i les armes pròpies s'afigen després en la fitxa de cada arcabusser.",
        ]);

    public static readonly ImportTemplateTexts English = new(
        SupportedLocales.English,
        DataSheet: "Arquebusiers",
        InstructionsSheet: "Instructions",
        ListsSheet: "Lists",
        Headers: new Dictionary<ImportColumn, string>
        {
            [ImportColumn.FederationId] = "Federation ID",
            [ImportColumn.LastName] = "Last names",
            [ImportColumn.FirstName] = "First name",
            [ImportColumn.NationalId] = "DNI/NIE",
            [ImportColumn.BirthDate] = "Date of birth",
            [ImportColumn.Gender] = "Gender",
            [ImportColumn.Email] = "Email",
            [ImportColumn.Phone] = "Phone",
            [ImportColumn.Status] = "Status",
            [ImportColumn.LicenseType] = "License type",
            [ImportColumn.LicenseIssuedOn] = "Issue date",
            [ImportColumn.LicenseExpiresOn] = "Expiry date",
            [ImportColumn.TrainingCompletedOn] = "Course date",
        },
        Descriptions: new Dictionary<ImportColumn, string>
        {
            [ImportColumn.FederationId] = "The arquebusier's code in the Federation's app (1 to 999999999). It cannot be repeated.",
            [ImportColumn.LastName] = "Up to 100 characters.",
            [ImportColumn.FirstName] = "Up to 100 characters.",
            [ImportColumn.NationalId] = "DNI: 8 digits and the letter; missing leading zeros are added. NIE: X, Y or Z, 7 digits and the letter. It cannot be repeated.",
            [ImportColumn.BirthDate] = "A date (dd/mm/yyyy).",
            [ImportColumn.Gender] = "Male, Female or Unspecified. Only for equality reports.",
            [ImportColumn.Email] = "An email address.",
            [ImportColumn.Phone] = "Digits and spaces, with an optional leading +; up to 20 characters.",
            [ImportColumn.Status] = "Active or Reserve. Empty means Active.",
            [ImportColumn.LicenseType] = "AE or A-PROF. Empty when there is no license. Without dates, the license is pending.",
            [ImportColumn.LicenseIssuedOn] = "A date (dd/mm/yyyy), only with a license type. It cannot be in the future.",
            [ImportColumn.LicenseExpiresOn] = "A date (dd/mm/yyyy), only with an issue date. Empty: 5 years later for AE and 1 year for A-PROF.",
            [ImportColumn.TrainingCompletedOn] = "The date (dd/mm/yyyy) the arquebusier course was done. Empty when it is not done.",
        },
        Genders: new Dictionary<Gender, string>
        {
            [Gender.Male] = "Male",
            [Gender.Female] = "Female",
            [Gender.Unspecified] = "Unspecified",
        },
        Statuses: new Dictionary<ArquebusierStatus, string>
        {
            [ArquebusierStatus.Active] = "Active",
            [ArquebusierStatus.Reserve] = "Reserve",
        },
        ColumnHeading: "Column",
        RequiredHeading: "Required",
        DescriptionHeading: "What to enter",
        Yes: "Yes",
        No: "No",
        Notes:
        [
            "One file per comparsa: the comparsa is chosen when importing.",
            "Fill in the \"Arquebusiers\" sheet from row 2, without changing the header row.",
            "Only .xlsx files of up to 2 MB and 1000 arquebusiers.",
            "Dates as dates or as dd/mm/yyyy text.",
            "Only new arquebusiers are registered: a row whose DNI/NIE or Federation ID is already registered is an error.",
            "If any row has an error, nothing is imported: fix the file and check it again.",
            "Photos and owned weapons are added afterwards on each arquebusier's page.",
        ]);

    public static readonly IReadOnlyList<ImportTemplateTexts> All = [Spanish, Valencian, English];

    /// <summary>The texts for <paramref name="culture"/> by its language, like the request culture: Spanish otherwise.</summary>
    public static ImportTemplateTexts For(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return culture.TwoLetterISOLanguageName switch
        {
            "ca" => Valencian,
            "en" => English,
            _ => Spanish,
        };
    }
}
