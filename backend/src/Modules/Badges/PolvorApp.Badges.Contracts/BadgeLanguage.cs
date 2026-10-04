using System.Text.Json.Serialization;
using PolvorApp.SharedKernel.Codes;

namespace PolvorApp.Badges.Contracts;

/// <summary>
/// The language of a badge sheet's labels, chosen by the Admin for each download while Q-49 is open
/// (spec: Badge language). Names, comparsas and identifiers are never translated.
/// </summary>
[JsonConverter(typeof(CodeEnumConverter<BadgeLanguage>))]
public enum BadgeLanguage
{
    [JsonStringEnumMemberName("es-ES")]
    Spanish,

    [JsonStringEnumMemberName("ca-ES-valencia")]
    Valencian,

    [JsonStringEnumMemberName("en")]
    English,
}
