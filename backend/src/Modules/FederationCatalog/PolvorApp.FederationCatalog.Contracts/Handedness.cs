using System.Text.Json.Serialization;
using PolvorApp.SharedKernel.Codes;

namespace PolvorApp.FederationCatalog.Contracts;

/// <summary>Weapon handedness (glossary: <c>Handedness</c>).</summary>
[JsonConverter(typeof(CodeEnumConverter<Handedness>))]
public enum Handedness
{
    [JsonStringEnumMemberName("RIGHT")]
    Right,

    [JsonStringEnumMemberName("LEFT")]
    Left,
}
