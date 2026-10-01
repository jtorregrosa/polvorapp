using System.Text.Json;
using PolvorApp.ArquebusierRegistry.NationalIds;

namespace PolvorApp.Api.Tests.Registry;

/// <summary>
/// BR-01 (spec: National ID validation): the shared synthetic vectors, also run by the frontend
/// tests, so the server and the form accept exactly the same values (design D4).
/// </summary>
public sealed class NationalIdTests
{
    public static TheoryData<string, string, string?, string?> Vectors()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestVectors", "national-ids.json")));
        var data = new TheoryData<string, string, string?, string?>();
        foreach (var vector in document.RootElement.GetProperty("cases").EnumerateArray())
        {
            data.Add(
                vector.GetProperty("name").GetString()!,
                vector.GetProperty("input").GetString()!,
                vector.TryGetProperty("normalized", out var normalized) ? normalized.GetString() : null,
                vector.TryGetProperty("error", out var error) ? error.GetString() : null);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Vectors), DisableDiscoveryEnumeration = true)]
    public void Shared_vectors(string name, string input, string? normalized, string? error)
    {
        var result = NationalId.Parse(input);

        Assert.True((normalized, error) == (result.Value, result.Error), $"{name}: expected ({normalized}, {error}), got ({result.Value}, {result.Error})");
    }

    [Fact]
    public void Absent_value_is_required()
    {
        Assert.Equal(NationalId.Required, NationalId.Parse(null).Error);
    }
}
