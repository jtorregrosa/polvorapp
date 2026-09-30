using System.Text.Json.Serialization;
using PolvorApp.SharedKernel.Codes;

namespace PolvorApp.FederationCatalog.Contracts;

/// <summary>Weapon size (glossary: <c>WeaponSize</c>).</summary>
[JsonConverter(typeof(CodeEnumConverter<WeaponSize>))]
public enum WeaponSize
{
    [JsonStringEnumMemberName("NORMAL")]
    Normal,

    [JsonStringEnumMemberName("SMALL")]
    Small,
}
