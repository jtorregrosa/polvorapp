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

    /// <summary>Whole years from <paramref name="birthDate"/> to <paramref name="referenceDate"/>.</summary>
    int AgeOn(DateOnly birthDate, DateOnly referenceDate);
}
