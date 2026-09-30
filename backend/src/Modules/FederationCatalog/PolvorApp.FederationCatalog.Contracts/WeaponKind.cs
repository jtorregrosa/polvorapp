using System.Text.Json.Serialization;
using PolvorApp.SharedKernel.Codes;

namespace PolvorApp.FederationCatalog.Contracts;

/// <summary>Weapon kind (glossary: <c>WeaponKind</c>). Pistols are never rentable (BR-07).</summary>
[JsonConverter(typeof(CodeEnumConverter<WeaponKind>))]
public enum WeaponKind
{
    [JsonStringEnumMemberName("TRABUCO")]
    Trabuco,

    [JsonStringEnumMemberName("ARCABUZ")]
    Arcabuz,

    [JsonStringEnumMemberName("PISTOL")]
    Pistol,
}
