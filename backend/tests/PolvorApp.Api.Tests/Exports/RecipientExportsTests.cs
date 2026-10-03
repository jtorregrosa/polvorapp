using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.Exports.Definitions;
using static PolvorApp.Api.Tests.Exports.ExportFixtures;

namespace PolvorApp.Api.Tests.Exports;

/// <summary>
/// The recipient exports from validated orders (specs: Powder supplier, Rental company and Arms
/// Authority exports; Orders included; SEC-06), in Spanish.
/// </summary>
public sealed class RecipientExportsTests
{
    private static readonly ExportTexts Texts = ExportTexts.Spanish;

    [Fact]
    public void Powder_supplier_has_totals_per_comparsa_and_no_personal_data()
    {
        var norte = Order(Norte, OrderStatus.Validated,
            Entry("Uno") with { PowderKg = 2, CapsBoxes = 3, CapsType = CapsType.Normal },
            Entry("Dos") with { PowderKg = 2, CapsBoxes = 1, CapsType = CapsType.Small },
            Entry("Tres") with { PowderKg = 1 });
        var este = Order(Este, OrderStatus.Validated, Entry("Cuatro") with { PowderKg = 2 });

        var table = new PowderSupplierExport().Build(Data(norte, este), Texts);

        Assert.Equal(["Comparsa", "Pólvora (kg)", "Cajas de pistones normales", "Cajas de pistones pequeños"], Headers(table));
        Assert.Equal(["Comparsa Sintética Este | 2 | 0 | 0", "Comparsa Sintética Norte | 5 | 3 | 1"], Lines(table));
        Assert.Equal("Total | 7 | 3 | 1", Line(table.TotalRow!));
        Assert.DoesNotContain(table.Rows.SelectMany(r => r), cell => cell is string text && (text.Contains("Uno", StringComparison.Ordinal) || text.Contains("00000001R", StringComparison.Ordinal)));
    }

    [Fact]
    public void Recipient_exports_are_provisional_and_named_after_the_edition()
    {
        var table = new PowderSupplierExport().Build(Data(), Texts);

        Assert.Equal("polvorapp-2031-powder-supplier-provisional", table.FileStem);
        Assert.Equal("Pedido de pólvora y pistones · Fiestas 2031", table.Title);
        Assert.Equal(["PROVISIONAL: formato pendiente de la plantilla del destinatario. No lo envíes como definitivo."], table.Notices);
        Assert.Equal("Definición powder-supplier, versión provisional-1", table.VersionLine);
        Assert.Empty(table.Rows);
        Assert.Equal("Total | 0 | 0 | 0", Line(table.TotalRow!));
    }

    [Fact]
    public void Rental_company_lists_only_weapon_and_flask_rentals()
    {
        var order = Order(Norte, OrderStatus.Validated,
            Entry("Zapata", nationalId: "00000003A") with { WeaponSource = WeaponSource.Rental, RentalWeaponModelId = Arcabuz, Flask = FlaskOption.Rental2Kg },
            Entry("Abad", nationalId: "00000004G") with { Flask = FlaskOption.Rental1Kg },
            Entry("Ortiz") with { WeaponSource = WeaponSource.Owned, Flask = FlaskOption.Owned });

        var table = new RentalCompanyExport().Build(Data(order), Texts);

        Assert.Equal(["Apellidos y nombre", "DNI/NIE", "Comparsa", "Modelo de arma", "Cantimplora"], Headers(table));
        Assert.Equal(
            [
                "Abad, Arcabucero | 00000004G | Comparsa Sintética Norte |  | 1 kg",
                "Zapata, Arcabucero | 00000003A | Comparsa Sintética Norte | ARCABUZ MORO DIESTRO | 2 kg",
            ],
            Lines(table));
        Assert.Null(table.TotalRow);
    }

    [Fact]
    public void Rows_follow_spanish_order_by_comparsa_then_person()
    {
        var norte = Order(Norte, OrderStatus.Validated,
            Entry("Ñúñez") with { Flask = FlaskOption.Rental1Kg },
            Entry("Nadal") with { Flask = FlaskOption.Rental1Kg });
        var este = Order(Este, OrderStatus.Validated, Entry("Zurita") with { Flask = FlaskOption.Rental1Kg });

        var names = new RentalCompanyExport().Build(Data(norte, este), Texts).Rows.Select(r => r[0]);

        Assert.Equal(["Zurita, Arcabucero", "Nadal, Arcabucero", "Ñúñez, Arcabucero"], names);
    }

    [Fact]
    public void Arms_authority_lists_active_entries_with_a_weapon()
    {
        var ownerId = Guid.NewGuid();
        var weaponId = Guid.NewGuid();
        var live = Live(ownerId, "Propietaria", new ArquebusierLicenseFacts.Issued(LicenseType.Ae, new DateOnly(2033, 5, 31), true, true),
            new RosterWeapon(weaponId, ownerId, Trabuco, "8-21", "GUIA-SINT-8"));
        var order = Order(Norte, OrderStatus.Validated,
            Entry("Copia", arquebusierId: ownerId) with { WeaponSource = WeaponSource.Owned, OwnedWeaponId = weaponId },
            Entry("Alquila", nationalId: "00000005M") with { WeaponSource = WeaponSource.Rental, RentalWeaponModelId = Arcabuz },
            Entry("Recibe", nationalId: "00000006Y") with
            {
                WeaponSource = WeaponSource.Loan,
                Loan = new ExportedLoan(LenderKind.External, new ExportedPerson("Presta", "Externo Sintético", "00000007F", null), null,
                    new ExportedWeapon(Arcabuz, "3-25", "GUIA-SINT-EXT")),
            },
            Entry("Sin Arma"),
            Entry("Reserva") with { IsActive = false });

        var table = new ArmsAuthorityExport().Build(Data([live], order), Texts);

        Assert.Equal(
            ["Apellidos y nombre", "DNI/NIE", "Comparsa", "Licencia", "Caducidad", "Arma", "Número de arma", "Guía de pertenencia", "Procedencia", "Cedente", "DNI/NIE del cedente"],
            Headers(table));
        Assert.Equal(ExportCellType.Date, table.Columns[4].Type);
        Assert.Equal(
            [
                "Alquila, Arcabucero | 00000005M | Comparsa Sintética Norte |  |  | ARCABUZ MORO DIESTRO |  |  | Alquiler |  | ",
                "Propietaria, Arcabucera | 00000002W | Comparsa Sintética Norte | AE | 2033-05-31 | TRABUCO CRISTIANO DIESTRO | 8-21 | GUIA-SINT-8 | Propia |  | ",
                "Recibe, Arcabucero | 00000006Y | Comparsa Sintética Norte |  |  | ARCABUZ MORO DIESTRO | 3-25 | GUIA-SINT-EXT | Cesión | Externo Sintético, Presta | 00000007F",
            ],
            Lines(table));
    }

    [Fact]
    public void Arms_authority_shows_a_pending_license_type_without_expiry()
    {
        var id = Guid.NewGuid();
        var live = Live(id, "Pendiente", new ArquebusierLicenseFacts.Pending(LicenseType.AProf));
        var order = Order(Norte, OrderStatus.Validated,
            Entry("Pendiente", arquebusierId: id) with { WeaponSource = WeaponSource.Rental, RentalWeaponModelId = Arcabuz });

        var row = Assert.Single(new ArmsAuthorityExport().Build(Data([live], order), Texts).Rows);

        Assert.Equal(("A-PROF", (object?)null), (row[3], row[4]));
    }

    [Fact]
    public void An_entry_no_longer_in_the_registry_uses_its_copy()
    {
        var order = Order(Norte, OrderStatus.Validated,
            Entry("Histórico", nationalId: "00000091E", arquebusierId: null) with
            {
                WeaponSource = WeaponSource.Owned,
                OwnedWeapon = new ExportedWeapon(Trabuco, "1-20", "GUIA-SINT-HIST"),
            });

        var line = Assert.Single(Lines(new ArmsAuthorityExport().Build(Data(order), Texts)));

        Assert.Equal("Histórico, Arcabucero | 00000091E | Comparsa Sintética Norte |  |  | TRABUCO CRISTIANO DIESTRO | 1-20 | GUIA-SINT-HIST | Propia |  | ", line);
    }
}
