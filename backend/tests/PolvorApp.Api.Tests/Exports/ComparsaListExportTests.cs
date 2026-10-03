using System.Globalization;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.Exports.Definitions;
using static PolvorApp.Api.Tests.Exports.ExportFixtures;

namespace PolvorApp.Api.Tests.Exports;

/// <summary>A comparsa's list (spec: Comparsa list export), in any order status and language.</summary>
public sealed class ComparsaListExportTests
{
    private static ExportedOrder Norte(OrderStatus status) => Order(ExportFixtures.Norte, status,
        Entry("Zapata", nationalId: "00000003A") with
        {
            PowderKg = 2,
            CapsBoxes = 3,
            CapsType = CapsType.Normal,
            WeaponSource = WeaponSource.Rental,
            RentalWeaponModelId = Arcabuz,
            Flask = FlaskOption.Rental2Kg,
        },
        Entry("Abad", nationalId: "00000004G") with
        {
            PowderKg = 1,
            WeaponSource = WeaponSource.Loan,
            Loan = new ExportedLoan(LenderKind.External, new ExportedPerson("Presta", "Externo Sintético", "00000007F", null), null,
                new ExportedWeapon(Trabuco, "3-25", "GUIA-SINT-EXT")),
        },
        Entry("Reserva", nationalId: "00000008P") with { IsActive = false });

    [Fact]
    public void Lists_every_entry_with_the_orders_totals()
    {
        var table = new ComparsaListExport().Build(Data(Norte(OrderStatus.Validated)), ExportTexts.Spanish);

        Assert.Equal(
            ["Apellidos y nombre", "DNI/NIE", "ID Unión", "Estado", "Pólvora (kg)", "Cajas de pistones", "Tipo de pistones", "Arma", "Cantimplora"],
            Headers(table));
        Assert.Equal(
            [
                "Abad, Arcabucero | 00000004G | 100001 | Activo | 1 | 0 |  | Cesión: TRABUCO CRISTIANO DIESTRO 3-25, cedida por Externo Sintético, Presta | Sin cantimplora",
                "Reserva, Arcabucero | 00000008P | 100001 | Reserva | 0 | 0 |  | Sin arma | Sin cantimplora",
                "Zapata, Arcabucero | 00000003A | 100001 | Activo | 2 | 3 | Normales | Alquiler: ARCABUZ MORO DIESTRO | 2 kg",
            ],
            Lines(table));
        Assert.Equal("Total |  |  |  | 3 | 3 |  |  | ", Line(table.TotalRow!));
    }

    [Fact]
    public void A_validated_order_is_not_a_draft()
    {
        var table = new ComparsaListExport().Build(Data(Norte(OrderStatus.Validated)), ExportTexts.Spanish);

        Assert.Equal("polvorapp-2031-comparsa-list-comparsa-sintetica-norte-provisional", table.FileStem);
        Assert.Equal("Pedido de Comparsa Sintética Norte · Fiestas 2031", table.Title);
        Assert.Single(table.Notices);
    }

    [Theory]
    [InlineData(OrderStatus.Draft, "en borrador")]
    [InlineData(OrderStatus.Submitted, "enviado")]
    [InlineData(OrderStatus.Returned, "devuelto")]
    public void An_order_not_validated_is_a_draft(OrderStatus status, string words)
    {
        var table = new ComparsaListExport().Build(Data(Norte(status)), ExportTexts.Spanish);

        Assert.Equal("polvorapp-2031-comparsa-list-comparsa-sintetica-norte-draft-provisional", table.FileStem);
        Assert.Equal($"BORRADOR: el pedido está {words} y aún no está validado; puede cambiar.", table.Notices[0]);
    }

    [Theory]
    [InlineData("ca-ES-valencia", "Cognoms i nom", "Comanda de Comparsa Sintética Norte · Festes 2031", "Reserva", "Sense arma")]
    [InlineData("en", "Surnames and name", "Order of Comparsa Sintética Norte · Festival 2031", "Reserve", "No weapon")]
    public void Follows_the_users_language(string culture, string nameHeader, string title, string reserve, string noWeapon)
    {
        var table = new ComparsaListExport().Build(Data(Norte(OrderStatus.Validated)), ExportTexts.For(CultureInfo.GetCultureInfo(culture)));

        Assert.Equal((nameHeader, title), (table.Columns[0].Header, table.Title));
        Assert.Equal((reserve, noWeapon), (table.Rows[1][3], table.Rows[1][7]));
    }
}
