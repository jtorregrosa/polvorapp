using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.Exports.Definitions;

namespace PolvorApp.Api.Tests.Exports;

/// <summary>Synthetic export data: invented comparsas, people, numbers and guides.</summary>
internal static class ExportFixtures
{
    public static readonly Guid Norte = Guid.Parse("00000000-0000-4000-8000-000000000101");
    public static readonly Guid Este = Guid.Parse("00000000-0000-4000-8000-000000000103");
    public static readonly Guid Arcabuz = Guid.Parse("00000000-0000-4000-8000-000000000601");
    public static readonly Guid Trabuco = Guid.Parse("00000000-0000-4000-8000-000000000602");

    public static readonly IReadOnlyDictionary<Guid, string> Comparsas = new Dictionary<Guid, string>
    {
        [Norte] = "Comparsa Sintética Norte",
        [Este] = "Comparsa Sintética Este",
    };

    public static readonly IReadOnlyDictionary<Guid, string> Models = new Dictionary<Guid, string>
    {
        [Arcabuz] = "ARCABUZ MORO DIESTRO",
        [Trabuco] = "TRABUCO CRISTIANO DIESTRO",
    };

    /// <summary>An <c>ACTIVE</c> entry with nothing ordered, whose copy names <paramref name="lastName"/>.</summary>
    public static ExportedEntry Entry(string lastName, string firstName = "Arcabucero", string nationalId = "00000001R", Guid? arquebusierId = null) =>
        new(Guid.NewGuid(), arquebusierId, IsActive: true, PowderKg: 0, CapsBoxes: 0, CapsType: null, WeaponSource.None, OwnedWeaponId: null,
            RentalWeaponModelId: null, FlaskOption.None, new ExportedPerson(firstName, lastName, nationalId, 100001), OwnedWeapon: null, Loan: null);

    public static ExportedOrder Order(Guid comparsaId, OrderStatus status, params ExportedEntry[] entries) =>
        new(Guid.NewGuid(), comparsaId, status, entries);

    public static ExportData Data(params ExportedOrder[] orders) => Data([], orders);

    public static ExportData Data(IReadOnlyList<RosterArquebusier> live, params ExportedOrder[] orders) =>
        new(2031, orders, Comparsas, Models, live.ToDictionary(a => a.Id));

    public static RosterArquebusier Live(
        Guid id, string lastName, ArquebusierLicenseFacts? license, params RosterWeapon[] weapons) =>
        new(id, Norte, "Arcabucera", lastName, "00000002W", 100002, ArquebusierStatus.Active, new DateOnly(1990, 1, 1), license, null, true, weapons);

    /// <summary>The rows as text, one line per row, cells joined by " | " (dates as yyyy-MM-dd, empty for null).</summary>
    public static string[] Lines(ExportTable table) => [.. table.Rows.Select(Line)];

    public static string Line(IReadOnlyList<object?> cells) => string.Join(" | ", cells.Select(cell => cell switch
    {
        null => "",
        DateOnly date => date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
        _ => Convert.ToString(cell, System.Globalization.CultureInfo.InvariantCulture),
    }));

    public static string[] Headers(ExportTable table) => [.. table.Columns.Select(c => c.Header)];
}
