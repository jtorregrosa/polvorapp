using System.Text.Json.Serialization;
using PolvorApp.SharedKernel.Codes;

namespace PolvorApp.ArquebusierRegistry.Contracts;

/// <summary>
/// Status of the current license, derived and never stored (glossary: <c>LicenseStatus</c>). An
/// arquebusier without a license has no status. "Expiring soon" is a compliance warning, not a status.
/// </summary>
[JsonConverter(typeof(CodeEnumConverter<LicenseStatus>))]
public enum LicenseStatus
{
    /// <summary>Applied for but not issued yet: no dates.</summary>
    [JsonStringEnumMemberName("PENDING")]
    Pending,

    /// <summary>Today, in Europe/Madrid, is on or before the expiry date.</summary>
    [JsonStringEnumMemberName("VALID")]
    Valid,

    /// <summary>The expiry date has passed.</summary>
    [JsonStringEnumMemberName("EXPIRED")]
    Expired,
}
