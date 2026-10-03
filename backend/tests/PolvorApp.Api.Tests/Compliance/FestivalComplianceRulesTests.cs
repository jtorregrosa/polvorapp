using PolvorApp.ComplianceInsights;
using PolvorApp.ComplianceInsights.Contracts;

namespace PolvorApp.Api.Tests.Compliance;

/// <summary>
/// Spec "Compliance warnings of edition entries (BR-04)" (add-comparsa-orders, design D7): the license
/// is checked through the last festival day, the age on the first one, and an expiry after the festival
/// is not a warning.
/// </summary>
public sealed class FestivalComplianceRulesTests
{
    private static readonly DateOnly StartsOn = new(2031, 4, 22);
    private static readonly DateOnly EndsOn = new(2031, 4, 25);
    private static readonly ComplianceRules Rules = new();

    /// <summary>40 years old at the festival, course done, ID photo, issued license with both photos valid through 2033.</summary>
    private static readonly ComplianceFacts Compliant = new(
        BirthDate: new DateOnly(1991, 1, 10),
        License: new ComplianceLicense.Issued(new DateOnly(2033, 1, 1), HasFrontPhoto: true, HasBackPhoto: true),
        TrainingCompletedOn: new DateOnly(2020, 11, 15),
        HasIdPhoto: true);

    private static IReadOnlyList<ComplianceWarning> Evaluate(ComplianceFacts facts) => Rules.EvaluateForFestival(facts, StartsOn, EndsOn);

    private static ComplianceFacts WithExpiry(DateOnly expiresOn) =>
        Compliant with { License = new ComplianceLicense.Issued(expiresOn, HasFrontPhoto: true, HasBackPhoto: true) };

    [Fact]
    public void Compliant_entry_has_no_warning() => Assert.Empty(Evaluate(Compliant));

    [Fact]
    public void License_expiring_during_the_festival_is_expired() =>
        Assert.Equal([ComplianceWarning.LicenseExpired], Evaluate(WithExpiry(new DateOnly(2031, 4, 23))));

    [Fact]
    public void License_valid_through_the_last_festival_day_has_no_warning() =>
        Assert.Empty(Evaluate(WithExpiry(EndsOn)));

    [Fact]
    public void License_expiring_soon_after_the_festival_is_not_a_warning() =>
        Assert.Empty(Evaluate(WithExpiry(new DateOnly(2031, 5, 1))));

    [Fact]
    public void License_expired_before_the_festival_is_expired() =>
        Assert.Equal([ComplianceWarning.LicenseExpired], Evaluate(WithExpiry(new DateOnly(2030, 12, 31))));

    [Fact]
    public void Missing_and_pending_licenses_keep_their_warnings()
    {
        Assert.Equal([ComplianceWarning.LicenseMissing], Evaluate(Compliant with { License = null }));
        Assert.Equal([ComplianceWarning.LicensePending], Evaluate(Compliant with { License = new ComplianceLicense.Pending() }));
    }

    [Fact]
    public void Age_is_evaluated_on_the_first_festival_day()
    {
        Assert.Empty(Evaluate(Compliant with { BirthDate = new DateOnly(2013, 4, 22) }));
        Assert.Equal([ComplianceWarning.UnderAge], Evaluate(Compliant with { BirthDate = new DateOnly(2013, 4, 23) }));
    }

    [Fact]
    public void Course_and_photos_are_evaluated_as_they_are()
    {
        var facts = Compliant with
        {
            TrainingCompletedOn = null,
            HasIdPhoto = false,
            License = new ComplianceLicense.Issued(new DateOnly(2033, 1, 1), HasFrontPhoto: true, HasBackPhoto: false),
        };

        Assert.Equal(
            [ComplianceWarning.CourseMissing, ComplianceWarning.IdPhotoMissing, ComplianceWarning.LicensePhotosMissing],
            Evaluate(facts));
    }

    [Fact]
    public void The_registry_rules_still_report_expiring_soon_on_their_reference_date() =>
        Assert.Equal([ComplianceWarning.LicenseExpiring], Rules.Evaluate(WithExpiry(new DateOnly(2031, 5, 1)), EndsOn));
}
