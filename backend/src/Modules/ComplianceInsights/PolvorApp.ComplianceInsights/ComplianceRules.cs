using PolvorApp.ComplianceInsights.Contracts;

namespace PolvorApp.ComplianceInsights;

/// <summary>
/// The BR-04 rules (design D2): stateless, so one instance serves every request. Dates follow
/// <see cref="DateOnly.AddYears"/> and <see cref="DateOnly.AddMonths"/>, which map 29 February to
/// 28 February in other years.
/// </summary>
internal sealed class ComplianceRules : IComplianceRules
{
    /// <summary>Legal age in Spain; arquebusiers must have reached it (maintainer decision, Q-54).</summary>
    public const int LegalAge = 18;

    /// <summary>A valid license expiring within this many months is "expiring soon".</summary>
    public const int ExpiringWindowMonths = 12;

    public IReadOnlyList<ComplianceWarning> Evaluate(ComplianceFacts facts, DateOnly referenceDate)
    {
        ArgumentNullException.ThrowIfNull(facts);

        var warnings = new List<ComplianceWarning>(capacity: 4);
        if (LicenseWarning(facts.License, referenceDate) is { } license)
        {
            warnings.Add(license);
        }

        if (facts.TrainingCompletedOn is null)
        {
            warnings.Add(ComplianceWarning.CourseMissing);
        }

        if (AgeOn(facts.BirthDate, referenceDate) < LegalAge)
        {
            warnings.Add(ComplianceWarning.UnderAge);
        }

        if (!facts.HasIdPhoto)
        {
            warnings.Add(ComplianceWarning.IdPhotoMissing);
        }

        if (facts.License is ComplianceLicense.Issued { HasFrontPhoto: false } or ComplianceLicense.Issued { HasBackPhoto: false })
        {
            warnings.Add(ComplianceWarning.LicensePhotosMissing);
        }

        return warnings.Count == 0 ? [] : warnings.ToArray();
    }

    /// <summary>
    /// Whole years; a birthday on 29 February falls on 28 February in other years. A birth date after
    /// the reference date gives a negative age, which counts as under age.
    /// </summary>
    public int AgeOn(DateOnly birthDate, DateOnly referenceDate)
    {
        var age = referenceDate.Year - birthDate.Year;
        return birthDate.AddYears(age) > referenceDate ? age - 1 : age;
    }

    /// <summary>At most one license warning; valid through the expiry day, like the license status.</summary>
    private static ComplianceWarning? LicenseWarning(ComplianceLicense? license, DateOnly referenceDate) => license switch
    {
        null => ComplianceWarning.LicenseMissing,
        ComplianceLicense.Pending => ComplianceWarning.LicensePending,
        ComplianceLicense.Issued issued when referenceDate > issued.ExpiresOn => ComplianceWarning.LicenseExpired,
        ComplianceLicense.Issued issued when issued.ExpiresOn < referenceDate.AddMonths(ExpiringWindowMonths) => ComplianceWarning.LicenseExpiring,
        ComplianceLicense.Issued => null,
        _ => throw new InvalidOperationException($"Unknown license shape {license.GetType().Name}."),
    };
}
