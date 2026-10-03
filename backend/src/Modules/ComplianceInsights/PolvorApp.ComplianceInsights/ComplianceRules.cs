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
        return Warnings(facts, LicenseWarning(facts.License, referenceDate, withExpiring: true), referenceDate);
    }

    /// <summary>Entries of an edition (add-comparsa-orders, design D7): the license through the last day, the age on the first.</summary>
    public IReadOnlyList<ComplianceWarning> EvaluateForFestival(ComplianceFacts facts, DateOnly startsOn, DateOnly endsOn)
    {
        ArgumentNullException.ThrowIfNull(facts);
        return Warnings(facts, LicenseWarning(facts.License, endsOn, withExpiring: false), startsOn);
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

    /// <summary>The warnings in rule order, given the license warning and the date the age is taken on.</summary>
    private ComplianceWarning[] Warnings(ComplianceFacts facts, ComplianceWarning? license, DateOnly ageDate)
    {
        var warnings = new List<ComplianceWarning>(capacity: 4);
        if (license is { } licenseWarning)
        {
            warnings.Add(licenseWarning);
        }

        if (facts.TrainingCompletedOn is null)
        {
            warnings.Add(ComplianceWarning.CourseMissing);
        }

        if (AgeOn(facts.BirthDate, ageDate) < LegalAge)
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
    /// At most one license warning; valid through the expiry day, like the license status. The
    /// festival evaluation leaves out "expiring soon" (<paramref name="withExpiring"/> false).
    /// </summary>
    private static ComplianceWarning? LicenseWarning(ComplianceLicense? license, DateOnly referenceDate, bool withExpiring) => license switch
    {
        null => ComplianceWarning.LicenseMissing,
        ComplianceLicense.Pending => ComplianceWarning.LicensePending,
        ComplianceLicense.Issued issued when referenceDate > issued.ExpiresOn => ComplianceWarning.LicenseExpired,
        ComplianceLicense.Issued issued when withExpiring && issued.ExpiresOn < referenceDate.AddMonths(ExpiringWindowMonths) => ComplianceWarning.LicenseExpiring,
        ComplianceLicense.Issued => null,
        _ => throw new InvalidOperationException($"Unknown license shape {license.GetType().Name}."),
    };
}
