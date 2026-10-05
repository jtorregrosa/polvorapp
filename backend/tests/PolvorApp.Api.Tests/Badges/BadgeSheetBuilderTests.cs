using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.Badges.Batches;
using PolvorApp.Badges.Contracts;
using PolvorApp.Badges.Documents;
using PolvorApp.Exports.Contracts;
using PolvorApp.FederationCatalog.Contracts;

namespace PolvorApp.Api.Tests.Badges;

/// <summary>
/// The badge content (spec: Badge content, Badge language, Incomplete badges are warnings; design D4,
/// D5): the labels and header words per language, the Federation's name, the values in print order,
/// empty lines for a missing expiry, the file name, and nothing else of the person.
/// </summary>
public sealed class BadgeSheetBuilderTests
{
    private static readonly DateOnly Today = new(2031, 3, 2);

    [Theory]
    [InlineData(BadgeLanguage.Spanish, "ARCABUCERO", "Apellidos", "Nombre", "Código", "Fecha de caducidad")]
    [InlineData(BadgeLanguage.Valencian, "ARCABUSSER", "Cognoms", "Nom", "Codi", "Data de caducitat")]
    [InlineData(BadgeLanguage.English, "ARQUEBUSIER", "Surnames", "Name", "Code", "Expiry date")]
    public void The_labels_follow_the_chosen_language(
        BadgeLanguage language, string headerWord, string surnames, string name, string code, string expiry)
    {
        var sheet = Build(BadgeBatchKind.Comparsa, language, [Person()]);

        Assert.Equal(headerWord, sheet.HeaderWord);
        Assert.Equal([surnames, name, "DNI/NIE", code, expiry, "Comparsa"], sheet.Labels);
    }

    [Theory]
    [InlineData(BadgeLanguage.Spanish, FederationNameForm.Spanish)]
    [InlineData(BadgeLanguage.Valencian, FederationNameForm.Valencian)]
    [InlineData(BadgeLanguage.English, FederationNameForm.Spanish)]
    public void The_language_picks_the_form_of_the_Federation_name(BadgeLanguage language, FederationNameForm form) =>
        Assert.Equal(form, BadgeTexts.For(language).NameForm);

    [Fact]
    public void The_header_prints_the_Federation_name_it_is_given()
    {
        var sheet = BadgeSheetBuilder.Build(new BadgeSheetContent(
            BadgeBatchKind.Comparsa, "Comparsa Sintética Norte", BadgeLanguage.Valencian, "Unió Sintètica de Comparses", Today, [Person()], null));

        Assert.Equal("Unió Sintètica de Comparses", sheet.HeaderLine);
    }

    [Fact]
    public void Values_are_the_registry_values_untranslated_with_the_expiry_as_a_date()
    {
        var sheet = Build(BadgeBatchKind.Comparsa, BadgeLanguage.English, [Person()]);

        var badge = Assert.Single(sheet.Badges);
        Assert.Equal(["Sintético Pérez", "Ana María", "00000000T", "123456789", "31/05/2033", "Comparsa Sintética Norte"], badge.Values);
    }

    [Fact]
    public void A_pending_or_missing_license_gives_an_empty_expiry_line_and_an_expired_one_its_date()
    {
        var sheet = Build(BadgeBatchKind.Selection, BadgeLanguage.Spanish,
        [
            Person(license: new ArquebusierLicenseFacts.Pending(LicenseType.Ae)),
            Person(defaultLicense: false),
            Person(license: new ArquebusierLicenseFacts.Issued(LicenseType.Ae, new DateOnly(2020, 1, 31), true, true)),
        ]);

        Assert.Null(sheet.Badges[0].Values[4]);
        Assert.Null(sheet.Badges[1].Values[4]);
        Assert.Equal("31/01/2020", sheet.Badges[2].Values[4]);
    }

    [Fact]
    public void The_photo_and_logo_are_passed_on_and_a_missing_photo_stays_empty()
    {
        var photo = DocumentImage.FromJpeg(TestImages.Jpeg(300, 400));
        var logo = DocumentImage.FromPng(TestImages.Png(40, 40));

        var sheet = BadgeSheetBuilder.Build(
            new BadgeSheetContent(BadgeBatchKind.Comparsa, "Comparsa Sintética Norte", BadgeLanguage.Spanish, TestFederationNames.Spanish, Today, [Person(photo: photo), Person()], logo));

        Assert.Same(photo, sheet.Badges[0].Photo);
        Assert.Null(sheet.Badges[1].Photo);
        Assert.Same(logo, sheet.Logo);
    }

    [Fact]
    public void The_order_of_the_people_is_kept()
    {
        var sheet = Build(BadgeBatchKind.Selection, BadgeLanguage.Spanish, [Person(lastName: "Zapata"), Person(lastName: "Abad")]);

        Assert.Equal(["Zapata", "Abad"], sheet.Badges.Select(b => b.Values[0]));
    }

    [Fact]
    public void File_names_hold_no_personal_data()
    {
        var comparsa = Build(BadgeBatchKind.Comparsa, BadgeLanguage.Spanish, [Person()]);
        var selection = Build(BadgeBatchKind.Selection, BadgeLanguage.Spanish, [Person(), Person(), Person()]);
        var longName = BadgeSheetBuilder.Build(new BadgeSheetContent(
            BadgeBatchKind.Comparsa, "Comparsa Sintética de los Moros Viejos del Raval de la Villa de Ejemplo", BadgeLanguage.Spanish, TestFederationNames.Spanish, Today, [Person()], null));

        Assert.Equal("polvorapp-badges-comparsa-sintetica-norte-20310302", comparsa.FileStem);
        Assert.Equal("polvorapp-badges-selection-3-20310302", selection.FileStem);
        Assert.Equal("polvorapp-badges-comparsa-sintetica-de-los-moros-viejos-d-20310302", longName.FileStem);
        Assert.Equal("Carnets de arcabucero", comparsa.Title);
    }

    [Fact]
    public void A_comparsa_name_without_letters_still_gives_a_file_name() =>
        Assert.Equal("polvorapp-badges-comparsa-20310302",
            BadgeSheetBuilder.Build(new BadgeSheetContent(BadgeBatchKind.Comparsa, "«»", BadgeLanguage.Spanish, TestFederationNames.Spanish, Today, [Person()], null)).FileStem);

    private static DocumentBadgeSheet Build(BadgeBatchKind kind, BadgeLanguage language, IReadOnlyList<BadgePerson> people) =>
        BadgeSheetBuilder.Build(new BadgeSheetContent(kind, "Comparsa Sintética Norte", language, TestFederationNames.Spanish, Today, people, null));

    private static BadgePerson Person(
        string lastName = "Sintético Pérez", ArquebusierLicenseFacts? license = null, DocumentImage? photo = null, bool defaultLicense = true) =>
        new(lastName, "Ana María", "00000000T", 123456789, "Comparsa Sintética Norte",
            license ?? (defaultLicense ? new ArquebusierLicenseFacts.Issued(LicenseType.Ae, new DateOnly(2033, 5, 31), true, true) : null),
            photo);
}
