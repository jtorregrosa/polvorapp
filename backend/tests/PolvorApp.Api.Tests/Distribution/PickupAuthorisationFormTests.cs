using System.Globalization;
using Microsoft.Extensions.Time.Testing;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.Distribution.Contracts;
using PolvorApp.Distribution.Documents;
using PolvorApp.Exports.Contracts;
using PolvorApp.Exports.Writers;
using UglyToad.PdfPig;

namespace PolvorApp.Api.Tests.Distribution;

/// <summary>Spec "Pickup authorisation form (UC-19)" (design D6): the pre-filled form to sign on paper.</summary>
public sealed class PickupAuthorisationFormTests
{
    private static readonly Guid ProxyId = Guid.Parse("0192f0aa-1234-7000-8000-000000000001");

    [Fact]
    public void The_powder_form_names_both_people_and_leaves_the_reason_place_and_signatures_blank()
    {
        var form = PickupAuthorisationForm.Build(Data(DistributionType.Powder), DistributionTexts.Spanish);

        Assert.Equal("Autorización de recogida de pólvora — Fiestas 2031", form.Title);
        Assert.Equal([DistributionTexts.Spanish.FederationName], form.HeadingLines);
        Assert.Equal(
            [
                ("Titular", null),
                ("Apellidos y nombre:", "Abad Sintética, Ana"),
                ("DNI/NIE:", "00000000T"),
                ("Licencia de armas:", "AE"),
                ("Comparsa:", "Comparsa Sintética Norte"),
                ("¶", "Ante la imposibilidad de recoger la pólvora que me corresponde el día del reparto (18/04/2031, Paraje Sintético), por el motivo que indico:"),
                ("Motivo:", null),
                ("¶", "AUTORIZO a recogerla en mi nombre, en las Fiestas 2031, a:"),
                ("Autorizado", null),
                ("Apellidos y nombre:", "Zamora Sintético, Berta"),
                ("DNI/NIE:", "00000001R"),
                ("Licencia de armas:", "A-PROF"),
                ("¶", "Perteneciente a la misma comparsa."),
                ("Lugar y fecha:", null),
            ],
            form.Blocks.Select(Describe));
        Assert.Equal(["Firma del titular", "Firma del autorizado"], form.SignatureLabels);
        Assert.Equal("pickup-authorisation, versión 1", form.VersionLine);
    }

    [Fact]
    public void The_file_name_has_the_year_type_comparsa_and_a_part_of_the_proxy_id_but_no_person() =>
        Assert.Equal(
            "polvorapp-2031-pickup-authorisation-weapons-comparsa-sintetica-norte-0192f0aa",
            PickupAuthorisationForm.Build(Data(DistributionType.Weapons), DistributionTexts.Spanish).FileStem);

    [Fact]
    public void A_comparsa_with_the_longest_name_still_gives_a_valid_file_name()
    {
        var data = Data(DistributionType.Powder) with { ComparsaName = string.Concat(Enumerable.Repeat("Comparsa Sintética ", 6))[..100] };

        var stem = PickupAuthorisationForm.Build(data, DistributionTexts.Spanish).FileStem;

        Assert.True(DocumentFileStem.IsValid(stem), stem);
        Assert.EndsWith("-0192f0aa", stem, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_a_planned_day_the_statement_names_no_date_and_a_missing_license_type_is_blank()
    {
        var data = Data(DistributionType.Weapons) with { Date = null, Location = null, Holder = new FormPerson("Abad Sintética, Ana", "00000000T", null) };

        var form = PickupAuthorisationForm.Build(data, DistributionTexts.Spanish);

        Assert.Contains(form.Blocks.OfType<DocumentFormParagraph>(), p => p.Text == "Ante la imposibilidad de recoger el arma de alquiler que me corresponde el día del reparto, por el motivo que indico:");
        Assert.Equal(("Licencia de armas:", null), Describe(form.Blocks[3]));
    }

    [Fact]
    public void The_logo_is_passed_when_uploaded()
    {
        var logo = DocumentImage.FromPng(TestImages.Png(400, 300));

        Assert.Same(logo, PickupAuthorisationForm.Build(Data(DistributionType.Powder) with { Logo = logo }, DistributionTexts.Spanish).Logo);
        Assert.Null(PickupAuthorisationForm.Build(Data(DistributionType.Powder), DistributionTexts.Spanish).Logo);
    }

    [Theory]
    [InlineData("ca-ES-valencia", "Autorització de recollida de pólvora — Festes 2031", "Signatura del titular")]
    [InlineData("en", "Authorisation to collect the powder — Festival 2031", "Holder's signature")]
    public void The_form_follows_the_users_language(string culture, string title, string signature)
    {
        var form = PickupAuthorisationForm.Build(Data(DistributionType.Powder), DistributionTexts.For(CultureInfo.GetCultureInfo(culture)));

        Assert.Equal((title, signature), (form.Title, form.SignatureLabels[0]));
    }

    [Theory]
    [InlineData("es-ES")]
    [InlineData("ca-ES-valencia")]
    [InlineData("en")]
    public void The_form_fits_one_page_with_long_names_and_the_logo(string culture)
    {
        var data = Data(DistributionType.Weapons) with
        {
            ComparsaName = "Comparsa Sintética de Nombre Particularmente Largo para Probar",
            Holder = new FormPerson("Apellidoprimero Apellidosegundo-Compuesto Sintética, Ana María de los Ángeles", "00000000T", LicenseType.Ae),
            Location = "Paraje Sintético de las Afueras, junto al Camino Viejo",
            Logo = DocumentImage.FromPng(TestImages.Png(1024, 512)),
        };
        var renderer = new DocumentRenderer(new FakeTimeProvider(new DateTimeOffset(2031, 4, 1, 10, 0, 0, TimeSpan.Zero)));

        var rendered = renderer.RenderForm(PickupAuthorisationForm.Build(data, DistributionTexts.For(CultureInfo.GetCultureInfo(culture))));

        using var pdf = PdfDocument.Open(rendered.Content.ToArray());
        Assert.Equal(1, pdf.NumberOfPages);
    }

    internal static PickupFormData Data(DistributionType type) => new(
        2031,
        type,
        ProxyId,
        "Comparsa Sintética Norte",
        new FormPerson("Abad Sintética, Ana", "00000000T", LicenseType.Ae),
        new FormPerson("Zamora Sintético, Berta", "00000001R", LicenseType.AProf),
        new DateOnly(2031, 4, 18),
        "Paraje Sintético",
        null);

    private static (string, string?) Describe(DocumentFormBlock block) => block switch
    {
        DocumentFormHeading heading => (heading.Text, null),
        DocumentFormParagraph paragraph => ("¶", paragraph.Text),
        DocumentFormField field => (field.Label, field.Value),
        _ => throw new InvalidOperationException("Unknown block."),
    };
}
