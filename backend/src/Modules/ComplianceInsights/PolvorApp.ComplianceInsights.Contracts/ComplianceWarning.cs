using System.Text.Json.Serialization;
using PolvorApp.SharedKernel.Codes;

namespace PolvorApp.ComplianceInsights.Contracts;

/// <summary>
/// A compliance warning of an arquebusier (BR-04), derived and never stored. Members are declared in
/// rule order, the order in which they are returned and shown. A warning never blocks anything.
/// </summary>
[JsonConverter(typeof(CodeEnumConverter<ComplianceWarning>))]
public enum ComplianceWarning
{
    /// <summary>The arquebusier has no license.</summary>
    [JsonStringEnumMemberName("LICENSE_MISSING")]
    LicenseMissing,

    /// <summary>The license is applied for but not issued yet.</summary>
    [JsonStringEnumMemberName("LICENSE_PENDING")]
    LicensePending,

    /// <summary>The license expired before the reference date.</summary>
    [JsonStringEnumMemberName("LICENSE_EXPIRED")]
    LicenseExpired,

    /// <summary>The license is valid but expires in less than 12 months.</summary>
    [JsonStringEnumMemberName("LICENSE_EXPIRING")]
    LicenseExpiring,

    /// <summary>The training course is not done.</summary>
    [JsonStringEnumMemberName("COURSE_MISSING")]
    CourseMissing,

    /// <summary>The arquebusier is younger than 18.</summary>
    [JsonStringEnumMemberName("UNDER_AGE")]
    UnderAge,

    /// <summary>There is no ID photo, which the badge needs.</summary>
    [JsonStringEnumMemberName("ID_PHOTO_MISSING")]
    IdPhotoMissing,

    /// <summary>The license is issued but lacks its front photo, its back photo or both.</summary>
    [JsonStringEnumMemberName("LICENSE_PHOTOS_MISSING")]
    LicensePhotosMissing,
}
