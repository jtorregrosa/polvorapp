using System.Globalization;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.Distribution.Contracts;
using PolvorApp.Distribution.Documents;
using PolvorApp.Exports.Contracts;

namespace PolvorApp.Api.Tests.Distribution;

/// <summary>Spec "Distribution lists (UC-20)" (design D6): which holders each list has, its columns and proxies, in three languages.</summary>
public sealed class DistributionListTests
{
    internal static readonly Guid Norte = Guid.Parse("0192f0aa-0000-7000-8000-00000000000a");
    internal static readonly Guid Sur = Guid.Parse("0192f0aa-0000-7000-8000-00000000000b");
    internal static readonly Guid Model = Guid.Parse("0192f0aa-0000-7000-8000-00000000000c");

    [Fact]
    public void The_powder_list_has_active_holders_with_powder_and_the_proxy_of_each()
    {
        var holder = Entry(powderKg: 2, flask: FlaskOption.Rental2Kg, last: "Abad Sintética");
        var noPowder = Entry(powderKg: 0, last: "Cero Sintético");
        var reserve = Entry(active: false, last: "Zamora Sintético");
        var data = Data(DistributionType.Powder, [Order(Norte, holder, noPowder, reserve)], proxies: [new ListProxy(holder.EntryId, reserve.EntryId)]);

        var table = DistributionLists.Build(data, DistributionTexts.Spanish);

        var row = Assert.Single(table.Rows);
        Assert.Equal(
            new object?[] { 1, "09:00", "Comparsa Sintética Norte", "Abad Sintética, Ana", holder.Person.NationalId, 2, "Alquiler 2 kg", null, null, null, null, "Zamora Sintético, Ana", reserve.Person.NationalId },
            row);
        Assert.Equal(
            ["Nº", "Turno", "Comparsa", "Apellidos y nombre", "DNI/NIE", "Kg", "Cantimplora", "Nº cantimplora", "Trazabilidad 1", "Trazabilidad 2", "Recogida por", "Autorizado", "DNI/NIE autorizado"],
            table.Columns.Select(c => c.Header));
        Assert.Equal(["Nº cantimplora", "Trazabilidad 1", "Trazabilidad 2"], table.Columns.Where(c => c.ForHandwriting).Select(c => c.Header));
        Assert.Equal("polvorapp-2031-powder-distribution-list", table.FileStem);
        Assert.Equal("Reparto de pólvora — Fiestas 2031", table.Title);
        Assert.Equal(["Día: 18/04/2031 · Lugar: Paraje Sintético", DistributionTexts.Spanish.NumberingNotice, "Entregas registradas: 0"], table.Notices);
        Assert.Equal("powder-distribution-list, versión 2", table.VersionLine);
    }

    [Theory]
    [InlineData("es-ES", "Autorizado", "Titular", "Entregas registradas: 2")]
    [InlineData("ca-ES-valencia", "Autoritzat", "Titular", "Entregues registrades: 2")]
    [InlineData("en", "Proxy", "Holder", "Handovers recorded: 2")]
    public void The_powder_list_shows_what_each_handover_recorded(string culture, string byProxy, string byHolder, string count)
    {
        var first = Entry(powderKg: 2, flask: FlaskOption.Rental2Kg, last: "Abad Sintética");
        var second = Entry(powderKg: 1, flask: FlaskOption.Owned, last: "Bernabeu Sintético");
        var pending = Entry(powderKg: 1, last: "Climent Sintética");
        var handovers = new Dictionary<Guid, ListHandover>
        {
            [first.EntryId] = new("P-117", "A3", null, ByProxy: true),
            [second.EntryId] = new(null, null, "B9", ByProxy: false),
        };
        var data = Data(DistributionType.Powder, [Order(Norte, first, second, pending)]) with { Handovers = handovers };

        var table = DistributionLists.Build(data, DistributionTexts.For(CultureInfo.GetCultureInfo(culture)));

        Assert.Equal(["P-117", "A3", null, byProxy], table.Rows[0].Skip(7).Take(4));
        Assert.Equal([null, null, "B9", byHolder], table.Rows[1].Skip(7).Take(4));
        Assert.Equal([null, null, null, null], table.Rows[2].Skip(7).Take(4));
        Assert.Equal(count, table.Notices[^1]);
    }

    [Fact]
    public void A_handover_of_a_holder_no_longer_in_the_list_is_not_counted()
    {
        var holder = Entry(powderKg: 1, last: "Abad Sintética");
        var data = Data(DistributionType.Powder, [Order(Norte, holder)]) with
        {
            Handovers = new Dictionary<Guid, ListHandover> { [Guid.CreateVersion7()] = new("P-117", null, null, ByProxy: false) },
        };

        var table = DistributionLists.Build(data, DistributionTexts.Spanish);

        Assert.Equal("Entregas registradas: 0", table.Notices[^1]);
        Assert.Contains(table.Columns, c => c.ForHandwriting);
    }

    [Fact]
    public void The_weapons_list_says_nothing_about_handovers()
    {
        var rental = Entry(source: WeaponSource.Rental, last: "Alquila Sintética");

        var table = DistributionLists.Build(Data(DistributionType.Weapons, [Order(Norte, rental)]), DistributionTexts.Spanish);

        Assert.Equal(["Día: 18/04/2031 · Lugar: Paraje Sintético", DistributionTexts.Spanish.NumberingNotice], table.Notices);
        Assert.DoesNotContain("Recogida por", table.Columns.Select(c => c.Header));
    }

    [Fact]
    public void The_weapons_list_has_rentals_only_with_the_model_and_an_empty_weapon_number()
    {
        var rental = Entry(source: WeaponSource.Rental, last: "Alquila Sintética");
        var owned = Entry(source: WeaponSource.Owned, last: "Propia Sintética");
        var lent = Entry(source: WeaponSource.Loan, last: "Prestada Sintética");
        var data = Data(DistributionType.Weapons, [Order(Norte, rental, owned, lent)]);

        var table = DistributionLists.Build(data, DistributionTexts.Spanish);

        Assert.Equal(new object?[] { 1, "09:00", "Comparsa Sintética Norte", "Alquila Sintética, Ana", rental.Person.NationalId, "ARCABUZ MORO DIESTRO", null, null, null }, Assert.Single(table.Rows));
        Assert.Equal(["Nº de arma"], table.Columns.Where(c => c.ForHandwriting).Select(c => c.Header));
        Assert.Equal("polvorapp-2031-weapons-distribution-list", table.FileStem);
    }

    [Fact]
    public void A_proxy_whose_license_does_not_hold_on_the_day_is_left_out()
    {
        var holder = Entry(powderKg: 1, last: "Abad Sintética");
        var proxy = Entry(active: false, last: "Breve Sintética", licenseExpiresOn: new DateOnly(2031, 4, 10));

        var table = DistributionLists.Build(
            Data(DistributionType.Powder, [Order(Norte, holder, proxy)], proxies: [new ListProxy(holder.EntryId, proxy.EntryId)]), DistributionTexts.Spanish);

        var row = Assert.Single(table.Rows);
        Assert.Equal((null, null), (row[10], row[11]));
    }

    [Fact]
    public void Identities_come_from_the_registry_while_present_and_from_the_copy_otherwise()
    {
        var live = Entry(powderKg: 1, last: "Copia Antigua");
        var gone = Entry(powderKg: 1, last: "Borrada Sintética", inRegistry: false);
        var data = Data(DistributionType.Powder, [Order(Norte, live, gone)]) with
        {
            Arquebusiers = new Dictionary<Guid, RosterArquebusier> { [live.ArquebusierId!.Value] = Arquebusier(live.ArquebusierId.Value, "Nueva Sintética", "00000000T", null) },
        };

        var names = DistributionLists.Build(data, DistributionTexts.Spanish).Rows.Select(r => r[3]).ToList();

        Assert.Equal(["Borrada Sintética, Ana", "Nueva Sintética, Ana"], names);
    }

    [Fact]
    public void Holders_are_numbered_across_comparsas_by_slot()
    {
        var data = Data(
            DistributionType.Powder,
            [Order(Sur, Entry(powderKg: 1, last: "Bernabeu")), Order(Norte, Entry(powderKg: 1, last: "Zamora"), Entry(powderKg: 1, last: "Abad"))],
            slots: new Dictionary<Guid, TimeOnly> { [Norte] = new(9, 0), [Sur] = new(9, 30) });

        var rows = DistributionLists.Build(data, DistributionTexts.Spanish).Rows;

        Assert.Equal([(1, "Abad"), (2, "Zamora"), (3, "Bernabeu")], rows.Select(r => ((int)r[0]!, ((string)r[3]!).Split(',')[0])));
    }

    [Fact]
    public void A_comparsa_without_a_slot_has_an_empty_slot_cell() =>
        Assert.Null(Assert.Single(DistributionLists.Build(
            Data(DistributionType.Powder, [Order(Sur, Entry(powderKg: 1))], slots: new Dictionary<Guid, TimeOnly>()), DistributionTexts.Spanish).Rows)[1]);

    [Fact]
    public void An_erased_copy_prints_the_placeholder_and_is_counted()
    {
        var erased = Entry(powderKg: 1, inRegistry: false) with { Person = new ExportedPerson(null, null, null, null) };

        var built = DistributionLists.BuildWithCounts(Data(DistributionType.Powder, [Order(Norte, erased)]), DistributionTexts.Spanish);

        Assert.Equal("[datos borrados]", Assert.Single(built.Table.Rows)[3]);
        Assert.Equal(1, built.ErasedNames);
    }

    [Fact]
    public void Proxies_left_out_are_counted_by_reason()
    {
        var holder = Entry(powderKg: 1, last: "Abad Sintética");
        var other = Entry(powderKg: 1, last: "Bernabeu Sintético");
        var expired = Entry(active: false, last: "Breve Sintética", licenseExpiresOn: new DateOnly(2031, 4, 10));
        var data = Data(
            DistributionType.Powder,
            [Order(Norte, holder, other, expired)],
            proxies: [new ListProxy(holder.EntryId, expired.EntryId), new ListProxy(other.EntryId, Guid.CreateVersion7())]);

        var built = DistributionLists.BuildWithCounts(data, DistributionTexts.Spanish);

        Assert.Equal((1, 1), (built.ProxiesWithoutLicense, built.ProxiesWithoutEntry));
        Assert.All(built.Table.Rows, row => Assert.Null(row[10]));
    }

    [Fact]
    public void An_edition_without_validated_orders_gives_headings_and_no_rows() =>
        Assert.Empty(DistributionLists.Build(Data(DistributionType.Weapons, []), DistributionTexts.Spanish).Rows);

    [Theory]
    [InlineData("ca-ES-valencia", "Repartiment de pólvora — Festes 2031", "Cognoms i nom", "Lloguer 1 kg")]
    [InlineData("en", "Powder distribution — Festival 2031", "Last name, first name", "Rented 1 kg")]
    public void Words_follow_the_language_and_catalogue_values_do_not(string culture, string title, string nameHeading, string flask)
    {
        var data = Data(DistributionType.Powder, [Order(Norte, Entry(powderKg: 1, flask: FlaskOption.Rental1Kg))]);

        var table = DistributionLists.Build(data, DistributionTexts.For(CultureInfo.GetCultureInfo(culture)));

        Assert.Equal(title, table.Title);
        Assert.Equal(nameHeading, table.Columns[3].Header);
        Assert.Equal((flask, "Comparsa Sintética Norte"), (Assert.Single(table.Rows)[6], table.Rows[0][2]));
    }

    [Fact]
    public void A_list_holds_no_license_contact_or_birth_data()
    {
        var table = DistributionLists.Build(Data(DistributionType.Powder, [Order(Norte, Entry(powderKg: 1))]), DistributionTexts.Spanish);

        var text = string.Join("|", table.Columns.Select(c => c.Header).Concat(table.Rows.SelectMany(r => r.Select(c => c?.ToString()))));
        Assert.DoesNotContain("2033", text, StringComparison.Ordinal);
        Assert.DoesNotContain("1990", text, StringComparison.Ordinal);
        Assert.DoesNotContain("AE", text, StringComparison.Ordinal);
    }

    internal static DistributionListData Data(
        DistributionType type,
        IReadOnlyList<ExportedOrder> orders,
        IReadOnlyList<ListProxy>? proxies = null,
        IReadOnlyDictionary<Guid, TimeOnly>? slots = null) => new(
        2031,
        type,
        new DateOnly(2031, 4, 18),
        "Paraje Sintético",
        slots ?? new Dictionary<Guid, TimeOnly> { [Norte] = new(9, 0), [Sur] = new(9, 30) },
        orders,
        new Dictionary<Guid, string> { [Norte] = "Comparsa Sintética Norte", [Sur] = "Comparsa Sintética Sur" },
        new Dictionary<Guid, string> { [Model] = "ARCABUZ MORO DIESTRO" },
        orders.SelectMany(o => o.Entries).Where(e => e.ArquebusierId is not null && Licenses.ContainsKey(e.EntryId))
            .ToDictionary(e => e.ArquebusierId!.Value, e => Arquebusier(e.ArquebusierId!.Value, e.Person.LastName!, e.Person.NationalId!, Licenses[e.EntryId])),
        proxies ?? []);

    private static readonly Dictionary<Guid, DateOnly?> Licenses = [];
    private static int _nationalIds;

    internal static ExportedEntry Entry(
        bool active = true,
        int powderKg = 0,
        WeaponSource source = WeaponSource.None,
        FlaskOption flask = FlaskOption.None,
        string last = "Sintética Prueba",
        DateOnly? licenseExpiresOn = null,
        bool inRegistry = true)
    {
        var entryId = Guid.CreateVersion7();
        Licenses[entryId] = licenseExpiresOn ?? new DateOnly(2033, 12, 31);
        if (!inRegistry)
        {
            Licenses.Remove(entryId);
        }

        // Synthetic DNIs with a valid check letter: 00000000T, 00000001R, …
        var number = Interlocked.Increment(ref _nationalIds) - 1;
        var nationalId = $"{number:D8}{"TRWAGMYFPDXBNJZSQVHLCKE"[number % 23]}";
        return new ExportedEntry(
            entryId, inRegistry ? Guid.CreateVersion7() : null, active, active ? powderKg : 0, 0, null,
            active ? source : WeaponSource.None, null, source == WeaponSource.Rental ? Model : null, active ? flask : FlaskOption.None,
            new ExportedPerson("Ana", last, nationalId, 100000 + number), null, null);
    }

    internal static ExportedOrder Order(Guid comparsa, params ExportedEntry[] entries) => new(Guid.CreateVersion7(), comparsa, OrderStatus.Validated, entries);

    private static RosterArquebusier Arquebusier(Guid id, string lastName, string nationalId, DateOnly? expiresOn) => new(
        id, Norte, "Ana", lastName, nationalId, 1, ArquebusierStatus.Active, new DateOnly(1990, 5, 1),
        expiresOn is { } expiry ? new ArquebusierLicenseFacts.Issued(LicenseType.Ae, expiry, true, true) : null, null, true, []);
}
