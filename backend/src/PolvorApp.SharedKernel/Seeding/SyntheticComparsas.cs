using System.Globalization;

namespace PolvorApp.SharedKernel.Seeding;

/// <summary>A seeded comparsa: invented, plausible festival names (realistic-seed-data, design D2).</summary>
/// <param name="Number">1–4 for the scenarios, 5–20 for the full dataset; the last part of the fixed id.</param>
/// <param name="Name">The invented name; never that of a comparsa of San Vicente del Raspeig.</param>
/// <param name="Christian">The side: Christian, or Moorish.</param>
/// <param name="Active">Only Zegríes is inactive.</param>
/// <param name="HasLogo">Only Abencerrajes has no logo (the platform's sidebar placeholder case).</param>
public sealed record SyntheticComparsa(int Number, string Name, bool Christian, bool Active, bool HasLogo)
{
    /// <summary>The fixed id the catalogue, registry, order and distribution seeders share.</summary>
    public Guid Id => new($"0193a100-0000-7000-8000-{Number.ToString("D12", CultureInfo.InvariantCulture)}");
}

/// <summary>
/// The seeded comparsas, shared by the seeders of every module (a module cannot reference another).
/// The four scenario comparsas keep the ids and roles of the former "Comparsa Sintética" ones:
/// Cruzados (Norte: both FiringChiefs, dark logo), Abencerrajes (Sur: no logo), Hospitalarios (Este)
/// and Zegríes (Oeste: inactive). The full dataset adds sixteen more.
/// </summary>
public static class SyntheticComparsas
{
    public static readonly IReadOnlyList<SyntheticComparsa> Scenario =
    [
        new(1, "Cruzados", Christian: true, Active: true, HasLogo: true),
        new(2, "Abencerrajes", Christian: false, Active: true, HasLogo: false),
        new(3, "Hospitalarios", Christian: true, Active: true, HasLogo: true),
        new(4, "Zegríes", Christian: false, Active: false, HasLogo: true),
    ];

    public static readonly IReadOnlyList<SyntheticComparsa> Added =
    [
        .. new[] { "Tercios", "Ballesteros", "Corsarios", "Labradores", "Mozárabes", "Caballeros de Sant Jordi", "Almirantes", "Guardia del Rey" }
            .Select((name, index) => new SyntheticComparsa(5 + index, name, Christian: true, Active: true, HasLogo: true)),
        .. new[] { "Almohades", "Nazaríes", "Mudéjares", "Bereberes", "Beduinos", "Kábilas", "Califas", "Sarracenos" }
            .Select((name, index) => new SyntheticComparsa(13 + index, name, Christian: false, Active: true, HasLogo: true)),
    ];

    public static readonly IReadOnlyList<SyntheticComparsa> All = [.. Scenario, .. Added];

    /// <summary>The comparsas of <paramref name="dataset"/>.</summary>
    public static IReadOnlyList<SyntheticComparsa> Of(SeedDataset dataset) => dataset == SeedDataset.Full ? All : Scenario;

    public static SyntheticComparsa ByNumber(int number) =>
        All.FirstOrDefault(c => c.Number == number) ?? throw new ArgumentOutOfRangeException(nameof(number), number, "No seeded comparsa has this number.");
}
