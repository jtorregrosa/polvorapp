using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.Distribution.Contracts;
using PolvorApp.Distribution.Documents;
using PolvorApp.Distribution.Proxies;
using PolvorApp.Exports.Definitions;
using static PolvorApp.Api.Tests.Exports.ExportFixtures;

namespace PolvorApp.Api.Tests.Privacy;

/// <summary>
/// Spec exports "Erased entries in exports" and distribution "Erased entries in distribution": lists of
/// people leave an erased entry out, totals still count it, and it is neither a pickup holder nor a proxy.
/// </summary>
public sealed class ErasedEntriesDocumentTests
{
    private static ExportedEntry Erased(ExportedEntry entry) =>
        entry with { Person = new ExportedPerson(null, null, null, null), Erased = true };

    [Fact]
    public void The_comparsa_list_leaves_an_erased_entry_out_and_keeps_its_figures_in_the_totals()
    {
        var order = Order(Norte, OrderStatus.Validated,
            Entry("Presente", nationalId: "00000003A") with { PowderKg = 1 },
            Erased(Entry("Borrada") with { PowderKg = 2, CapsBoxes = 4, CapsType = CapsType.Normal }));

        var table = new ComparsaListExport().Build(Data(order), ExportTexts.Spanish);

        Assert.Single(table.Rows);
        Assert.Equal("Total |  |  |  | 3 | 4 |  |  | ", Line(table.TotalRow!));
    }

    [Fact]
    public void The_arms_authority_leaves_an_erased_entry_out_and_shows_an_erased_lender_as_the_model_only()
    {
        var order = Order(Norte, OrderStatus.Validated,
            Erased(Entry("Borrada") with { WeaponSource = WeaponSource.Rental, RentalWeaponModelId = Arcabuz }),
            Entry("Recibe", nationalId: "00000006Y") with
            {
                WeaponSource = WeaponSource.Loan,
                Loan = new ExportedLoan(LenderKind.External, new ExportedPerson(null, null, null, null), null, new ExportedWeapon(Arcabuz, null, null)),
            });

        var table = new ArmsAuthorityExport().Build(Data(order), ExportTexts.Spanish);

        Assert.Equal(["Recibe, Arcabucero | 00000006Y | Comparsa Sintética Norte |  |  | ARCABUZ MORO DIESTRO |  |  | Cesión |  | "], Lines(table));
    }

    [Fact]
    public void The_powder_list_leaves_an_erased_holder_out_and_numbers_without_a_gap()
    {
        var kept = Distribution.DistributionListTests.Entry(powderKg: 1, last: "Abad Sintética");
        var erased = Distribution.DistributionListTests.Entry(powderKg: 2, last: "Borrada Sintética") with { Erased = true };
        var data = Distribution.DistributionListTests.Data(
            DistributionType.Powder, [Distribution.DistributionListTests.Order(Distribution.DistributionListTests.Norte, erased, kept)]);

        var table = DistributionLists.Build(data, DistributionTexts.Spanish);

        var row = Assert.Single(table.Rows);
        Assert.Equal(1, row[0]);
        Assert.Equal("Abad Sintética, Ana", row[3]);
    }

    [Fact]
    public void An_erased_entry_is_neither_holder_nor_proxy()
    {
        var order = Guid.CreateVersion7();
        var holder = Facts(order, powderKg: 2);
        var proxy = Facts(order);

        Assert.Equal(("holderEntryId", ProxyRules.EntryErased), ProxyRules.CheckEntries(holder with { Erased = true }, proxy, DistributionType.Powder));
        Assert.Equal(("proxyEntryId", ProxyRules.EntryErased), ProxyRules.CheckEntries(holder, proxy with { Erased = true }, DistributionType.Powder));
        Assert.Null(ProxyRules.CheckEntries(holder, proxy, DistributionType.Powder));
    }

    private static EditionEntryFacts Facts(Guid order, int powderKg = 0) => new(
        Guid.CreateVersion7(), order, Guid.CreateVersion7(), Norte, OrderStatus.Validated, Guid.CreateVersion7(), true, powderKg,
        WeaponSource.None, null, FlaskOption.None, new ExportedPerson("Ana", "Abad Sintética", "00000000T", 1));
}
