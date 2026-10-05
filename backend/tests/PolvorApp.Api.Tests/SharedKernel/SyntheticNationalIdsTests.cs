using PolvorApp.SharedKernel.Seeding;
using PolvorApp.SharedKernel.Validation;

namespace PolvorApp.Api.Tests.SharedKernel;

/// <summary>
/// The seed's DNI/NIE ranges (realistic-seed-data, design D4; spec: Synthetic registry data): valid
/// check letters (BR-01), only 99xxxxxx DNIs and Z9xxxxxx NIEs, and never a repeat.
/// </summary>
public sealed class SyntheticNationalIdsTests
{
    [Theory]
    [InlineData(1, "99000001")]
    [InlineData(14, "99000014")]
    [InlineData(91, "99000091")]
    public void A_scenario_dni_uses_the_seeded_range_with_a_valid_letter(int number, string digits)
    {
        var dni = SyntheticNationalIds.ScenarioDni(number);

        Assert.StartsWith(digits, dni, StringComparison.Ordinal);
        Assert.Equal(dni, NationalId.Parse(dni).Value);
    }

    [Theory]
    [InlineData(5, "Z9000005")]
    [InlineData(10, "Z9000010")]
    public void A_scenario_nie_uses_the_seeded_range_with_a_valid_letter(int number, string digits)
    {
        var nie = SyntheticNationalIds.ScenarioNie(number);

        Assert.StartsWith(digits, nie, StringComparison.Ordinal);
        Assert.Equal(nie, NationalId.Parse(nie).Value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public void A_scenario_number_outside_1_to_99_is_refused(int number)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SyntheticNationalIds.ScenarioDni(number));
        Assert.Throws<ArgumentOutOfRangeException>(() => SyntheticNationalIds.ScenarioNie(number));
    }

    [Fact]
    public void Population_ids_are_valid_unique_and_inside_the_population_ranges()
    {
        var dnis = Enumerable.Range(0, 10_000).Select(SyntheticNationalIds.PopulationDni).ToList();
        var nies = Enumerable.Range(0, 10_000).Select(SyntheticNationalIds.PopulationNie).ToList();

        Assert.Equal(dnis.Count, dnis.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(nies.Count, nies.Distinct(StringComparer.Ordinal).Count());
        Assert.All(dnis, dni =>
        {
            Assert.Equal(dni, NationalId.Parse(dni).Value);
            Assert.InRange(int.Parse(dni[..8], System.Globalization.CultureInfo.InvariantCulture), 99_100_000, 99_999_999);
        });
        Assert.All(nies, nie =>
        {
            Assert.Equal(nie, NationalId.Parse(nie).Value);
            Assert.StartsWith("Z9", nie, StringComparison.Ordinal);
            Assert.InRange(int.Parse(nie[1..8], System.Globalization.CultureInfo.InvariantCulture), 9_100_000, 9_999_999);
        });
    }

    [Fact]
    public void Population_ids_do_not_look_sequential()
    {
        var first = int.Parse(SyntheticNationalIds.PopulationDni(0)[..8], System.Globalization.CultureInfo.InvariantCulture);
        var second = int.Parse(SyntheticNationalIds.PopulationDni(1)[..8], System.Globalization.CultureInfo.InvariantCulture);

        Assert.True(Math.Abs(second - first) > 1);
    }

    [Fact]
    public void A_negative_population_index_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SyntheticNationalIds.PopulationDni(-1));
    }
}
