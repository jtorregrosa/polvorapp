using Microsoft.Extensions.Configuration;

namespace PolvorApp.SharedKernel.Seeding;

/// <summary>Which synthetic data the seed creates (spec: Guarded synthetic seed; realistic-seed-data, design D1).</summary>
public enum SeedDataset
{
    /// <summary>The fixed cases each module's seed requirement lists, at their current size; the default.</summary>
    Scenarios,

    /// <summary>The scenarios plus a realistic population at the festival's scale.</summary>
    Full,
}

/// <summary>Reads the <c>Seed:Dataset</c> setting (<c>Seed__Dataset</c>, compose <c>SEED_DATASET</c>).</summary>
public static class SeedDatasets
{
    public const string Key = "Seed:Dataset";

    /// <summary>The configured dataset; <see cref="SeedDataset.Scenarios"/> when unset or blank.</summary>
    /// <exception cref="InvalidOperationException">The value names no dataset.</exception>
    public static SeedDataset Read(IConfiguration configuration)
    {
        var value = configuration[Key]?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return SeedDataset.Scenarios;
        }

        // Only the names: Enum.TryParse would also accept numbers and comma-separated flags.
        foreach (var dataset in Enum.GetValues<SeedDataset>())
        {
            if (string.Equals(value, dataset.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                return dataset;
            }
        }

        throw new InvalidOperationException(
            $"The {Key} setting (Seed__Dataset) is '{value}'; it must be {string.Join(" or ", Enum.GetNames<SeedDataset>())}.");
    }
}
