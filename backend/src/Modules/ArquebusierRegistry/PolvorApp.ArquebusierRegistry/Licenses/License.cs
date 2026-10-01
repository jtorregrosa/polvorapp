using PolvorApp.ArquebusierRegistry.Contracts;

namespace PolvorApp.ArquebusierRegistry.Licenses;

/// <summary>
/// The current license of an arquebusier (spec: Current license): at most one, without number or
/// history. Pending licenses have no dates; issued ones have both, with the expiry after the issue.
/// Stored in the <c>license_*</c> columns of the arquebusier row (design D3).
/// </summary>
internal sealed record License(LicenseType Type, bool Pending, DateOnly? IssuedOn, DateOnly? ExpiresOn)
{
    /// <summary>BR-03: AE licenses last 5 years and A-PROF ones 1 year; 29 February maps to 28 February.</summary>
    public static DateOnly DefaultExpiry(LicenseType type, DateOnly issuedOn) =>
        type == LicenseType.Ae ? issuedOn.AddYears(5) : issuedOn.AddYears(1);

    /// <summary>Derived, never stored: valid until the end of its expiry day in Europe/Madrid.</summary>
    public LicenseStatus StatusOn(DateOnly today) =>
        Pending ? LicenseStatus.Pending
        : today <= ExpiresOn ? LicenseStatus.Valid
        : LicenseStatus.Expired;
}
