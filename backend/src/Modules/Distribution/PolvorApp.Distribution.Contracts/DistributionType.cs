using System.Text.Json.Serialization;
using PolvorApp.SharedKernel.Codes;

namespace PolvorApp.Distribution.Contracts;

/// <summary>
/// What a distribution day hands out (spec: Distribution days (UC-18)), and what a pickup proxy collects
/// (spec: Pickup proxies (UC-19, BR-06)). An edition has at most one day of each type.
/// </summary>
[JsonConverter(typeof(CodeEnumConverter<DistributionType>))]
public enum DistributionType
{
    /// <summary>The powder, with the rented flasks.</summary>
    [JsonStringEnumMemberName("POWDER")]
    Powder,

    /// <summary>The rented weapons.</summary>
    [JsonStringEnumMemberName("WEAPONS")]
    Weapons,
}
