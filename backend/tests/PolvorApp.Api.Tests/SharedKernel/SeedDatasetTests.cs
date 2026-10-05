using Microsoft.Extensions.Configuration;
using PolvorApp.SharedKernel.Seeding;

namespace PolvorApp.Api.Tests.SharedKernel;

/// <summary>The seed's dataset setting (realistic-seed-data, design D1; spec: Guarded synthetic seed).</summary>
public sealed class SeedDatasetTests
{
    [Fact]
    public void The_scenarios_dataset_is_the_default()
    {
        Assert.Equal(SeedDataset.Scenarios, SeedDatasets.Read(Configuration(null)));
    }

    [Theory]
    [InlineData("Full", SeedDataset.Full)]
    [InlineData("full", SeedDataset.Full)]
    [InlineData(" FULL ", SeedDataset.Full)]
    [InlineData("scenarios", SeedDataset.Scenarios)]
    [InlineData("", SeedDataset.Scenarios)]
    public void A_known_dataset_is_read_case_insensitively(string value, SeedDataset expected)
    {
        Assert.Equal(expected, SeedDatasets.Read(Configuration(value)));
    }

    [Theory]
    [InlineData("huge")]
    [InlineData("1")]
    [InlineData("Full,Scenarios")]
    public void An_unknown_dataset_is_refused_naming_the_setting(string value)
    {
        var error = Assert.Throws<InvalidOperationException>(() => SeedDatasets.Read(Configuration(value)));

        Assert.Contains(SeedDatasets.Key, error.Message, StringComparison.Ordinal);
    }

    internal static IConfiguration Configuration(string? dataset) => new ConfigurationBuilder()
        .AddInMemoryCollection(dataset is null ? [] : [new KeyValuePair<string, string?>(SeedDatasets.Key, dataset)])
        .Build();
}
