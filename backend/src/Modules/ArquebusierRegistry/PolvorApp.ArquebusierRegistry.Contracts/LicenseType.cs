using System.Text.Json.Serialization;
using PolvorApp.SharedKernel.Codes;

namespace PolvorApp.ArquebusierRegistry.Contracts;

/// <summary>Type of the black-powder license (glossary: <c>LicenseType</c>).</summary>
[JsonConverter(typeof(CodeEnumConverter<LicenseType>))]
public enum LicenseType
{
    /// <summary>Muzzle-loading weapons license, valid 5 years (BR-03).</summary>
    [JsonStringEnumMemberName("AE")]
    Ae,

    /// <summary>Professional license, renewed every year (BR-03, Q-27).</summary>
    [JsonStringEnumMemberName("A_PROF")]
    AProf,
}
