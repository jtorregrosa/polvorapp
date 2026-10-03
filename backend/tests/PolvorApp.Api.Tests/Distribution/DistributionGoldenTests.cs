using System.Globalization;
using Microsoft.Extensions.Time.Testing;
using PolvorApp.Api.Tests.Exports;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.Distribution.Contracts;
using PolvorApp.Distribution.Documents;
using PolvorApp.Exports.Contracts;
using PolvorApp.Exports.Writers;

namespace PolvorApp.Api.Tests.Distribution;

/// <summary>
/// Golden files (ADR-0008; design D10) of the distribution lists in Excel and PDF and of the pickup
/// authorisation form, from one synthetic data set, in Spanish and in English. Set
/// <c>POLVORAPP_UPDATE_GOLDEN=1</c> to rewrite them, then review the diff.
/// </summary>
public sealed class DistributionGoldenTests
{
    private static readonly DocumentRenderer Renderer = new(new FakeTimeProvider(new DateTimeOffset(2031, 4, 10, 9, 0, 0, TimeSpan.Zero)));

    private static readonly Guid Norte = Guid.Parse("0192f0aa-0000-7000-8000-0000000000a1");
    private static readonly Guid Sur = Guid.Parse("0192f0aa-0000-7000-8000-0000000000a2");
    private static readonly Guid Este = Guid.Parse("0192f0aa-0000-7000-8000-0000000000a3");
    private static readonly Guid Model = Guid.Parse("0192f0aa-0000-7000-8000-0000000000b1");

    public static TheoryData<string, string, string> Cases => new()
    {
        { "powder", "es-ES", "xlsx" },
        { "powder", "es-ES", "pdf" },
        { "powder", "en", "pdf" },
        { "weapons", "es-ES", "xlsx" },
        { "weapons", "es-ES", "pdf" },
        { "weapons", "en", "xlsx" },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Each_list_matches_its_golden_file(string type, string culture, string format)
    {
        var data = Data(type == "powder" ? DistributionType.Powder : DistributionType.Weapons);
        var table = DistributionLists.Build(data, DistributionTexts.For(CultureInfo.GetCultureInfo(culture)));

        var document = Renderer.RenderTable(table, format == "pdf" ? DocumentFileFormat.Pdf : DocumentFileFormat.Xlsx);

        var text = format == "pdf" ? DocumentText.Pdf(document.Content.ToArray()) : DocumentText.Grid(document.Content.ToArray());
        Golden.Matches($"{type}-distribution-list-{culture}.{format}", text);
    }

    [Theory]
    [InlineData("es-ES")]
    [InlineData("ca-ES-valencia")]
    [InlineData("en")]
    public void The_form_matches_its_golden_file(string culture)
    {
        var form = PickupAuthorisationForm.Build(PickupAuthorisationFormTests.Data(DistributionType.Powder), DistributionTexts.For(CultureInfo.GetCultureInfo(culture)));

        Golden.Matches($"pickup-authorisation-{culture}.pdf", DocumentText.Pdf(Renderer.RenderForm(form).Content.ToArray()));
    }

    [Fact]
    public void The_same_data_on_the_same_day_gives_the_same_files()
    {
        var table = DistributionLists.Build(Data(DistributionType.Powder), DistributionTexts.Spanish);

        Assert.Equal(Renderer.RenderTable(table, DocumentFileFormat.Pdf).Content.ToArray(), Renderer.RenderTable(table, DocumentFileFormat.Pdf).Content.ToArray());
    }

    private static DistributionListData Data(DistributionType type)
    {
        var abad = Entry(1, "Abad Sintética", "Ana", powderKg: 2, flask: FlaskOption.Rental2Kg, rental: true);
        var zamora = Entry(2, "Zamora Sintético", "Bruno", powderKg: 1, flask: FlaskOption.Owned);
        var reserva = Entry(3, "Reserva Sintética", "Carla", active: false);
        var bernabeu = Entry(4, "Bernabeu Sintético", "Dani", powderKg: 1, rental: true);
        var climent = Entry(5, "Climent Sintética", "Eva", powderKg: 2, flask: FlaskOption.Rental1Kg);
        ExportedOrder[] orders =
        [
            new(Guid.Parse("0192f0aa-0000-7000-8000-0000000000c1"), Norte, OrderStatus.Validated, [abad, zamora, reserva]),
            new(Guid.Parse("0192f0aa-0000-7000-8000-0000000000c2"), Sur, OrderStatus.Validated, [bernabeu]),
            new(Guid.Parse("0192f0aa-0000-7000-8000-0000000000c3"), Este, OrderStatus.Validated, [climent]),
        ];
        return new DistributionListData(
            2031,
            type,
            type == DistributionType.Powder ? new DateOnly(2031, 4, 18) : new DateOnly(2031, 4, 5),
            type == DistributionType.Powder ? "Paraje Sintético" : "Almacén Sintético",
            new Dictionary<Guid, TimeOnly> { [Norte] = new(9, 0), [Sur] = new(9, 30) },
            orders,
            new Dictionary<Guid, string> { [Norte] = "Comparsa Sintética Norte", [Sur] = "Comparsa Sintética Sur", [Este] = "Comparsa Sintética Este" },
            new Dictionary<Guid, string> { [Model] = "ARCABUZ MORO DIESTRO" },
            orders.SelectMany(o => o.Entries).ToDictionary(e => e.ArquebusierId!.Value, e => Live(e)),
            [new ListProxy(abad.EntryId, reserva.EntryId), new ListProxy(bernabeu.EntryId, zamora.EntryId)]);
    }

    private static ExportedEntry Entry(int n, string last, string first, bool active = true, int powderKg = 0, FlaskOption flask = FlaskOption.None, bool rental = false)
    {
        var nationalId = $"{n:D8}{"TRWAGMYFPDXBNJZSQVHLCKE"[n % 23]}";
        return new ExportedEntry(
            Guid.Parse($"0192f0aa-0000-7000-8000-0000000001{n:D2}"),
            Guid.Parse($"0192f0aa-0000-7000-8000-0000000002{n:D2}"),
            active,
            powderKg,
            0,
            null,
            rental ? WeaponSource.Rental : WeaponSource.None,
            null,
            rental ? Model : null,
            flask,
            new ExportedPerson(first, last, nationalId, 100000 + n),
            null,
            null);
    }

    private static RosterArquebusier Live(ExportedEntry entry) => new(
        entry.ArquebusierId!.Value, Norte, entry.Person.FirstName!, entry.Person.LastName!, entry.Person.NationalId!, entry.Person.FederationId!.Value,
        ArquebusierStatus.Active, new DateOnly(1990, 5, 1), new ArquebusierLicenseFacts.Issued(LicenseType.Ae, new DateOnly(2033, 12, 31), true, true),
        null, true, []);
}
