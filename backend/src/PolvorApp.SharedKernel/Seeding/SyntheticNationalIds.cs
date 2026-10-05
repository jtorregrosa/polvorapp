using System.Globalization;

namespace PolvorApp.SharedKernel.Seeding;

/// <summary>
/// DNI and NIE values for the synthetic seed (realistic-seed-data, design D4; spec: Synthetic registry
/// data). Spain has no reserved fictional range, so the seed uses ranges with no evidence of use:
/// DNIs 99,000,000–99,999,999 and NIEs Z9,000,000–Z9,999,999, with BR-01 check letters. The DNI
/// numbers 1–99 belong to historical and royal holders and are never used. Scenarios take
/// 99,000,001–99,000,099 (Z9,000,001–Z9,000,099); the population draws from 99,100,000 upwards.
/// The automated tests create their own rows in other ranges (1,000+, 9,100,000+, 20M+, 30M+, 40M+,
/// 70M+), so seeded and test values never collide.
/// </summary>
public static class SyntheticNationalIds
{
    private const string Letters = "TRWAGMYFPDXBNJZSQVHLCKE";
    private const int DniBase = 99_000_000;
    private const int NieBase = 9_000_000;
    private const int PopulationOffset = 100_000;

    /// <summary>Size of the population ranges: 99,100,000–99,999,999 and Z9,100,000–Z9,999,999.</summary>
    private const int PopulationSize = 900_000;

    /// <summary>Coprime with <see cref="PopulationSize"/> (2⁵·3²·5⁵): (index + 1) × step mod size is a bijection, so draws never repeat, and the large step makes neighbours look unrelated.</summary>
    private const int Step = 611_953;

    /// <summary>The DNI of scenario <paramref name="number"/> (1–99): 990000NN plus its letter.</summary>
    public static string ScenarioDni(int number) => Dni(DniBase + ScenarioNumber(number));

    /// <summary>The NIE of scenario <paramref name="number"/> (1–99): Z90000NN plus its letter.</summary>
    public static string ScenarioNie(int number) => Nie(NieBase + ScenarioNumber(number));

    /// <summary>The DNI of the population's person <paramref name="index"/> (0-based, below 900,000).</summary>
    public static string PopulationDni(int index) => Dni(DniBase + PopulationNumber(index));

    /// <summary>The NIE of the population's person <paramref name="index"/> (0-based, below 900,000).</summary>
    public static string PopulationNie(int index) => Nie(NieBase + PopulationNumber(index));

    private static int ScenarioNumber(int number)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(number, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(number, 99);
        return number;
    }

    private static int PopulationNumber(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, PopulationSize);
        return PopulationOffset + (int)((index + 1L) * Step % PopulationSize);
    }

    private static string Dni(int number) =>
        number.ToString("D8", CultureInfo.InvariantCulture) + Letters[number % Letters.Length];

    /// <summary>Z stands for 2 in front of the seven digits (BR-01).</summary>
    private static string Nie(int number) =>
        "Z" + number.ToString("D7", CultureInfo.InvariantCulture) + Letters[(20_000_000 + number) % Letters.Length];
}
