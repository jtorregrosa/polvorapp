using System.Globalization;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.Exports.Definitions;
using static PolvorApp.Api.Tests.Exports.ExportFixtures;

namespace PolvorApp.Api.Tests.Exports;

/// <summary>What every definition relies on (design D3, D6): the table checks, words, names, order and lookups.</summary>
public sealed class ExportRulesTests
{
    public static TheoryData<string> Languages => ["es-ES", "ca-ES-valencia", "en"];

    [Fact]
    public void A_row_with_the_wrong_number_of_cells_is_refused()
    {
        var error = Assert.Throws<InvalidOperationException>(() => new ExportTable(
            "stem", "title", [], "version", [new("Nombre", ExportCellType.Text)], [["Secreto Sintético", 1]], null));

        Assert.DoesNotContain("Secreto", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_cell_of_the_wrong_type_is_refused_naming_the_column_not_the_value()
    {
        var error = Assert.Throws<InvalidOperationException>(() => new ExportTable(
            "stem", "title", [], "version", [new("Nombre", ExportCellType.Text), new("Pólvora", ExportCellType.Integer)], [["Ana", "Secreto"]], null));

        Assert.Contains("Pólvora", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Secreto", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_total_rows_label_may_stand_in_a_number_column() =>
        Assert.NotNull(new ExportTable("stem", "title", [], "version", [new("Kg", ExportCellType.Integer)], [[2]], ["Total"]));

    [Theory]
    [MemberData(nameof(Languages))]
    public void Every_code_has_a_word_in_every_language(string culture)
    {
        var texts = ExportTexts.For(CultureInfo.GetCultureInfo(culture));

        Assert.Equal(Enum.GetValues<OrderStatus>().Order(), texts.OrderStatuses.Keys.Order());
        Assert.Equal(Enum.GetValues<WeaponSource>().Order(), texts.WeaponSources.Keys.Order());
        Assert.Equal(Enum.GetValues<FlaskOption>().Order(), texts.Flasks.Keys.Order());
        Assert.Equal(Enum.GetValues<CapsType>().Order(), texts.CapsTypes.Keys.Order());
    }

    [Theory]
    [InlineData("ca-ES-valencia", "ESBORRANY: la comanda està en esborrany i encara no està validada; pot canviar.", "Definició comparsa-list, versió provisional-1")]
    [InlineData("en", "DRAFT: the order is a draft and not validated yet; it may change.", "Definition comparsa-list, version provisional-1")]
    public void Notices_and_version_line_follow_the_language(string culture, string draft, string version)
    {
        var table = new ComparsaListExport().Build(Data(Order(Norte, OrderStatus.Draft)), ExportTexts.For(CultureInfo.GetCultureInfo(culture)));

        Assert.Equal(draft, table.Notices[0]);
        Assert.StartsWith("PROVISIONAL", table.Notices[1], StringComparison.Ordinal);
        Assert.Equal(version, table.VersionLine);
    }

    [Theory]
    [InlineData("Comparsa Sintética Norte", "comparsa-sintetica-norte")]
    [InlineData("Cristians d'Aragó", "cristians-d-arago")]
    [InlineData("Ñ  Moros  Nuevos!", "n-moros-nuevos")]
    [InlineData("Col·la", "col-la")]
    public void Slugs_keep_ascii_letters_and_digits(string name, string slug) => Assert.Equal(slug, ExportRows.Slug(name));

    [Fact]
    public void A_name_without_letters_still_gives_a_file_name() =>
        Assert.Equal("polvorapp-2031-comparsa-list-comparsa-provisional", ExportRows.FileStem(2031, "comparsa-list", true, "·!"));

    [Fact]
    public void People_with_the_same_last_name_sort_by_first_name()
    {
        var order = Order(Norte, OrderStatus.Validated,
            Entry("Álvarez", "Bea") with { Flask = FlaskOption.Rental1Kg },
            Entry("Alvarez", "Ana") with { Flask = FlaskOption.Rental1Kg },
            Entry("Álvarez", "Ana", nationalId: "00000009D") with { Flask = FlaskOption.Rental1Kg });

        var names = new RentalCompanyExport().Build(Data(order), ExportTexts.Spanish).Rows.Select(r => r[0]);

        Assert.Equal(["Alvarez, Ana", "Álvarez, Ana", "Álvarez, Bea"], names);
    }

    [Fact]
    public void The_registry_identity_and_owned_weapon_win_over_the_copy()
    {
        var id = Guid.NewGuid();
        var weaponId = Guid.NewGuid();
        var live = Live(id, "Actualizada", null, new RosterWeapon(weaponId, id, Trabuco, "9-30", "GUIA-SINT-9"));
        var order = Order(Norte, OrderStatus.Validated,
            Entry("Antigua", arquebusierId: id) with
            {
                WeaponSource = WeaponSource.Owned,
                OwnedWeaponId = weaponId,
                OwnedWeapon = new ExportedWeapon(Arcabuz, "1-01", "GUIA-VIEJA"),
            });

        var row = Assert.Single(new ComparsaListExport().Build(Data([live], order), ExportTexts.Spanish).Rows);

        Assert.Equal(("Actualizada, Arcabucera", "00000002W", (object?)100002), (row[0], row[1], row[2]));
        Assert.Equal("Propia: TRABUCO CRISTIANO DIESTRO 9-30", row[7]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void A_comparsa_list_needs_exactly_one_order(int orders) =>
        Assert.Throws<ArgumentException>(() => new ComparsaListExport().Build(
            Data([.. Enumerable.Range(0, orders).Select(_ => Order(Norte, OrderStatus.Validated))]), ExportTexts.Spanish));

    [Fact]
    public void A_model_missing_from_the_catalogue_fails_rather_than_export_a_blank()
    {
        var order = Order(Norte, OrderStatus.Validated, Entry("Uno") with { WeaponSource = WeaponSource.Rental, RentalWeaponModelId = Guid.NewGuid() });

        Assert.Throws<InvalidOperationException>(() => new RentalCompanyExport().Build(Data(order), ExportTexts.Spanish));
    }

    [Fact]
    public void A_drafts_weapon_without_details_says_so()
    {
        var order = Order(Norte, OrderStatus.Draft, Entry("Uno") with { WeaponSource = WeaponSource.Loan });

        Assert.Equal("Cesión: sin datos", new ComparsaListExport().Build(Data(order), ExportTexts.Spanish).Rows[0][7]);
    }

    [Fact]
    public void An_arquebusier_without_a_license_reads_so_for_the_arms_authority()
    {
        var id = Guid.NewGuid();
        var order = Order(Norte, OrderStatus.Validated,
            Entry("Sin Licencia", arquebusierId: id) with { WeaponSource = WeaponSource.Rental, RentalWeaponModelId = Arcabuz });

        var row = Assert.Single(new ArmsAuthorityExport().Build(Data([Live(id, "Sin Licencia", null)], order), ExportTexts.Spanish).Rows);

        Assert.Equal(("Sin licencia", (object?)null), (row[3], row[4]));
    }

    [Fact]
    public void A_weapon_without_its_data_is_never_sent_blank_to_the_arms_authority()
    {
        var order = Order(Norte, OrderStatus.Validated, Entry("Uno") with { WeaponSource = WeaponSource.Loan });

        Assert.Throws<InvalidOperationException>(() => new ArmsAuthorityExport().Build(Data(order), ExportTexts.Spanish));
    }

    [Fact]
    public void Caps_without_a_type_are_never_dropped_from_the_supplier_order()
    {
        var order = Order(Norte, OrderStatus.Validated, Entry("Uno") with { CapsBoxes = 2 });

        Assert.Throws<InvalidOperationException>(() => new PowderSupplierExport().Build(Data(order), ExportTexts.Spanish));
    }
}
