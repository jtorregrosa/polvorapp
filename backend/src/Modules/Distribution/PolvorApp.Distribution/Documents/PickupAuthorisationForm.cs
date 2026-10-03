using System.Globalization;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.Distribution.Contracts;
using PolvorApp.Exports.Contracts;
using PolvorApp.SharedKernel.Codes;
using PolvorApp.SharedKernel.Text;

namespace PolvorApp.Distribution.Documents;

/// <summary>A person on the form: "Last name, First name", DNI/NIE and license type (null when they have none).</summary>
internal sealed record FormPerson(string Name, string? NationalId, LicenseType? LicenseType)
{
    /// <summary>The type name only: the members are personal data.</summary>
    public override string ToString() => nameof(FormPerson);
}

/// <summary>What one authorisation form is built from (design D6).</summary>
/// <param name="EditionYear">The edition's year.</param>
/// <param name="Type">What is collected.</param>
/// <param name="ProxyId">The proxy, a part of whose id names the file.</param>
/// <param name="ComparsaName">The holder's and the proxy's comparsa.</param>
/// <param name="Holder">The holder, from the registry at the time of the download.</param>
/// <param name="Proxy">The proxy, likewise.</param>
/// <param name="Date">The distribution day of that type, when planned.</param>
/// <param name="Location">Its location, when planned.</param>
/// <param name="Logo">The Federation's logo, when uploaded.</param>
internal sealed record PickupFormData(
    int EditionYear,
    DistributionType Type,
    Guid ProxyId,
    string ComparsaName,
    FormPerson Holder,
    FormPerson Proxy,
    DateOnly? Date,
    string? Location,
    DocumentImage? Logo)
{
    /// <summary>The type name only.</summary>
    public override string ToString() => nameof(PickupFormData);
}

/// <summary>
/// The pickup authorisation form (spec: Pickup authorisation form (UC-19); design D6), a pure function of
/// <see cref="PickupFormData"/>: the Federation's logo and name, the holder's and the proxy's sections, the
/// statement of the paper form, blank lines for the reason and the place and date, and both signatures —
/// in the reader's language. The reason is never stored: it is written by hand (maintainer decision).
/// </summary>
internal static class PickupAuthorisationForm
{
    public const string Name = "pickup-authorisation";
    public const string Version = "1";

    /// <summary>The comparsa part of the file name, so the whole stem stays within its 120 characters.</summary>
    public const int MaxComparsaSlug = 40;

    public static DocumentForm Build(PickupFormData data, DistributionTexts texts)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(texts);
        var day = data is { Date: { } date, Location: { } location }
            ? texts.Format(texts.PlannedDay, DistributionTexts.Date(date), location)
            : string.Empty;
        DocumentFormBlock[] blocks =
        [
            new DocumentFormHeading(texts.HolderHeading),
            .. Person(data.Holder, texts),
            new DocumentFormField(texts.FormComparsa, data.ComparsaName),
            new DocumentFormParagraph(texts.Format(texts.HolderStatement, texts.Collects[data.Type], day)),
            new DocumentFormField(texts.Reason, null),
            new DocumentFormParagraph(texts.Format(texts.AuthorisationStatement, data.EditionYear)),
            new DocumentFormHeading(texts.ProxyHeading),
            .. Person(data.Proxy, texts),
            new DocumentFormParagraph(texts.SameComparsa),
            new DocumentFormField(texts.PlaceAndDate, null),
        ];
        return new DocumentForm(
            FileStem(data),
            texts.Format(texts.FormTitles[data.Type], data.EditionYear),
            [texts.FederationName],
            blocks,
            [texts.HolderSignature, texts.ProxySignature],
            texts.Format(texts.VersionLine, Name, Version),
            data.Logo);
    }

    /// <summary>Year, type, comparsa and the first 8 hex digits of the proxy's id: never a person's name or DNI/NIE.</summary>
    public static string FileStem(PickupFormData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var slug = FileSlug.Of(data.ComparsaName);
        var comparsa = slug.Length > MaxComparsaSlug ? slug[..MaxComparsaSlug].TrimEnd('-') : slug;
        return string.Join('-',
            "polvorapp",
            data.EditionYear.ToString(CultureInfo.InvariantCulture),
            Name,
            EnumCodes.ToCode(data.Type).ToLowerInvariant(),
            comparsa.Length > 0 ? comparsa : "comparsa",
            data.ProxyId.ToString("N")[..8]);
    }

    private static DocumentFormField[] Person(FormPerson person, DistributionTexts texts) =>
    [
        new(texts.FormName, person.Name),
        new(texts.FormNationalId, person.NationalId),

        // As the Federation writes it: AE, A-PROF.
        new(texts.FormLicense, person.LicenseType is { } type ? EnumCodes.ToCode(type).Replace('_', '-') : null),
    ];
}
