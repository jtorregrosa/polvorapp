using System.Globalization;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.Exports.Contracts;
using PolvorApp.Exports.Definitions;
using static PolvorApp.Api.Tests.Exports.ExportFixtures;

namespace PolvorApp.Api.Tests.Exports;

/// <summary>The synthetic data set every golden file is generated from: one table per definition.</summary>
internal static class GoldenData
{
    private static readonly Guid OwnerId = Guid.Parse("00000000-0000-4000-8000-000000000301");
    private static readonly Guid WeaponId = Guid.Parse("00000000-0000-4000-8000-000000000401");

    private static readonly RosterArquebusier Owner = new(
        OwnerId, Norte, "Arcabucera", "Sintética Uno", "00000001R", 100001, ArquebusierStatus.Active, new DateOnly(1990, 1, 1),
        new ArquebusierLicenseFacts.Issued(LicenseType.Ae, new DateOnly(2033, 5, 31), true, true), null, true,
        [new RosterWeapon(WeaponId, OwnerId, Trabuco, "8-21", "GUIA-SINT-8")]);

    private static ExportedEntry Entry(int n, string lastName) => ExportFixtures.Entry(lastName, "Arcabucero", $"0000000{n}X") with
    {
        EntryId = Guid.Parse($"00000000-0000-4000-8000-00000000080{n}"),
        Person = new ExportedPerson("Arcabucero", lastName, $"0000000{n}X", 100000 + n),
    };

    public static ExportedOrder NorteOrder(OrderStatus status) => new(
        Guid.Parse("00000000-0000-4000-8000-000000000701"), Norte, status,
        [
            Entry(1, "Sintética Uno") with
            {
                ArquebusierId = OwnerId, PowderKg = 2, CapsBoxes = 3, CapsType = CapsType.Normal, WeaponSource = WeaponSource.Owned,
                OwnedWeaponId = WeaponId, Flask = FlaskOption.Rental2Kg,
            },
            Entry(2, "Sintético Dos") with
            {
                PowderKg = 1, CapsBoxes = 1, CapsType = CapsType.Small, WeaponSource = WeaponSource.Rental, RentalWeaponModelId = Arcabuz,
                Flask = FlaskOption.Rental1Kg,
            },
            Entry(3, "Sintético Tres") with
            {
                PowderKg = 2,
                WeaponSource = WeaponSource.Loan,
                Loan = new ExportedLoan(LenderKind.External, new ExportedPerson("Presta", "Externo Sintético", "00000007F", null), null,
                    new ExportedWeapon(Arcabuz, "3-25", "GUIA-SINT-EXT")),
            },
            Entry(4, "Sintético Cuatro") with { IsActive = false },
        ]);

    private static ExportedOrder EsteOrder => new(
        Guid.Parse("00000000-0000-4000-8000-000000000702"), Este, OrderStatus.Validated,
        [Entry(5, "Sintético Cinco") with { PowderKg = 1, CapsBoxes = 2, CapsType = CapsType.Normal, WeaponSource = WeaponSource.Rental, RentalWeaponModelId = Trabuco }]);

    private static ExportData All => Data([Owner], NorteOrder(OrderStatus.Validated), EsteOrder);

    /// <summary>Each golden case: its name and its table.</summary>
    public static TheoryData<string> Cases => ["powder-supplier", "rental-company", "arms-authority", "comparsa-list", "comparsa-list-draft-en"];

    public static DocumentTable Table(string name) => name switch
    {
        "powder-supplier" => new PowderSupplierExport().Build(All, ExportTexts.Spanish),
        "rental-company" => new RentalCompanyExport().Build(All, ExportTexts.Spanish),
        "arms-authority" => new ArmsAuthorityExport().Build(All, ExportTexts.Spanish),
        "comparsa-list" => new ComparsaListExport().Build(Data([Owner], NorteOrder(OrderStatus.Validated)), ExportTexts.Spanish),
        "comparsa-list-draft-en" => new ComparsaListExport().Build(
            Data([Owner], NorteOrder(OrderStatus.Submitted)), ExportTexts.For(CultureInfo.GetCultureInfo("en"))),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };
}
