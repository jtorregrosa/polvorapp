namespace PolvorApp.ComplianceInsights.Contracts;

/// <summary>
/// The compliance rules of BR-04 (spec: Compliance warnings). The single owner of the rules: the
/// registry calls it for its list and detail, and later modules call it with other reference dates
/// (e.g. the festival dates of an edition).
/// </summary>
public interface IComplianceRules
{
    /// <summary>The warnings of <paramref name="facts"/> on <paramref name="referenceDate"/>, in rule order.</summary>
    IReadOnlyList<ComplianceWarning> Evaluate(ComplianceFacts facts, DateOnly referenceDate);

    /// <summary>
    /// The warnings of an <c>ACTIVE</c> edition entry, in rule order (add-comparsa-orders, design D7):
    /// the license must be valid through <paramref name="endsOn"/>, the age is taken on
    /// <paramref name="startsOn"/>, and <see cref="ComplianceWarning.LicenseExpiring"/> never applies,
    /// because an expiry after the festival does not affect the order.
    /// </summary>
    IReadOnlyList<ComplianceWarning> EvaluateForFestival(ComplianceFacts facts, DateOnly startsOn, DateOnly endsOn);

    /// <summary>Whole years from <paramref name="birthDate"/> to <paramref name="referenceDate"/>.</summary>
    int AgeOn(DateOnly birthDate, DateOnly referenceDate);
}
