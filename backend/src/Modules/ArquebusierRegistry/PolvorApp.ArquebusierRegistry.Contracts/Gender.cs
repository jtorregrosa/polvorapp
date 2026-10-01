using System.Text.Json.Serialization;
using PolvorApp.SharedKernel.Codes;

namespace PolvorApp.ArquebusierRegistry.Contracts;

/// <summary>Gender of an arquebusier, kept for equality reports only (Q-32).</summary>
[JsonConverter(typeof(CodeEnumConverter<Gender>))]
public enum Gender
{
    [JsonStringEnumMemberName("MALE")]
    Male,

    [JsonStringEnumMemberName("FEMALE")]
    Female,

    [JsonStringEnumMemberName("UNSPECIFIED")]
    Unspecified,
}
