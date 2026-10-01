using System.Text.Json.Serialization;
using PolvorApp.SharedKernel.Codes;

namespace PolvorApp.ArquebusierRegistry.Contracts;

/// <summary>
/// The photos an arquebusier can have, one of each (spec: Arquebusier photos; glossary
/// <c>idPhoto</c>, <c>frontPhoto</c>, <c>backPhoto</c>). All are optional (maintainer decision).
/// </summary>
[JsonConverter(typeof(CodeEnumConverter<ArquebusierPhotoKind>))]
public enum ArquebusierPhotoKind
{
    /// <summary>Portrait photo printed on the arquebusier badge (UC-30): 3:4, at least 600 × 800 px (NFR-15).</summary>
    [JsonStringEnumMemberName("ID")]
    Id,

    /// <summary>Front of the current license; needs a license.</summary>
    [JsonStringEnumMemberName("LICENSE_FRONT")]
    LicenseFront,

    /// <summary>Back of the current license; needs a license.</summary>
    [JsonStringEnumMemberName("LICENSE_BACK")]
    LicenseBack,
}
