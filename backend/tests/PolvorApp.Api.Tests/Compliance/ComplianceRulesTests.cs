using System.Globalization;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ArquebusierRegistry.Licenses;
using PolvorApp.ComplianceInsights;
using PolvorApp.ComplianceInsights.Contracts;
using PolvorApp.SharedKernel.Codes;

namespace PolvorApp.Api.Tests.Compliance;

/// <summary>
/// The compliance warnings of BR-04 (spec: Compliance warnings), evaluated on a fixed reference
/// date. Every boundary of the spec has a case here.
/// </summary>
public sealed class ComplianceRulesTests
{
    private static readonly DateOnly Today = new(2026, 10, 2);
    private static readonly ComplianceRules Rules = new();

    /// <summary>30 years old, course done, ID photo, issued license with both photos valid for two years.</summary>
    private static readonly ComplianceFacts Compliant = new(
        BirthDate: new DateOnly(1996, 5, 1),
        License: new ComplianceLicense.Issued(new DateOnly(2028, 10, 2), HasFrontPhoto: true, HasBackPhoto: true),
        TrainingCompletedOn: new DateOnly(2020, 11, 15),
        HasIdPhoto: true);

    private static IReadOnlyList<ComplianceWarning> Evaluate(ComplianceFacts facts) => Rules.Evaluate(facts, Today);

    private static ComplianceFacts WithExpiry(DateOnly expiresOn) =>
        Compliant with { License = new ComplianceLicense.Issued(expiresOn, HasFrontPhoto: true, HasBackPhoto: true) };

    [Fact]
    public void Compliant_arquebusier_has_no_warning() => Assert.Empty(Evaluate(Compliant));

    [Fact]
    public void License_expired_yesterday() =>
        Assert.Equal([ComplianceWarning.LicenseExpired], Evaluate(WithExpiry(new DateOnly(2026, 10, 1))));

    [Fact]
    public void License_expiring_today_is_expiring_not_expired() =>
        Assert.Equal([ComplianceWarning.LicenseExpiring], Evaluate(WithExpiry(Today)));

    [Fact]
    public void License_expiring_one_day_before_twelve_months_is_expiring() =>
        Assert.Equal([ComplianceWarning.LicenseExpiring], Evaluate(WithExpiry(new DateOnly(2027, 10, 1))));

    [Fact]
    public void License_expiring_in_exactly_twelve_months_has_no_warning() =>
        Assert.Empty(Evaluate(WithExpiry(new DateOnly(2027, 10, 2))));

    [Fact]
    public void Expiring_window_from_29_February_ends_on_28_February()
    {
        var leapDay = new DateOnly(2028, 2, 29);

        Assert.Equal([ComplianceWarning.LicenseExpiring], Rules.Evaluate(WithExpiry(new DateOnly(2029, 2, 27)), leapDay));
        Assert.Empty(Rules.Evaluate(WithExpiry(new DateOnly(2029, 2, 28)), leapDay));
    }

    [Fact]
    public void Pending_license_is_only_pending_without_photo_warning() =>
        Assert.Equal([ComplianceWarning.LicensePending], Evaluate(Compliant with { License = new ComplianceLicense.Pending() }));

    [Fact]
    public void No_license_is_missing_without_photo_warning() =>
        Assert.Equal([ComplianceWarning.LicenseMissing], Evaluate(Compliant with { License = null }));

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void Issued_license_missing_a_photo(bool front, bool back) =>
        Assert.Equal(
            [ComplianceWarning.LicensePhotosMissing],
            Evaluate(Compliant with { License = new ComplianceLicense.Issued(new DateOnly(2028, 10, 2), front, back) }));

    [Fact]
    public void Expired_license_missing_a_photo_has_both_warnings() =>
        Assert.Equal(
            [ComplianceWarning.LicenseExpired, ComplianceWarning.LicensePhotosMissing],
            Evaluate(Compliant with { License = new ComplianceLicense.Issued(new DateOnly(2025, 1, 1), HasFrontPhoto: true, HasBackPhoto: false) }));

    [Fact]
    public void Course_not_done() =>
        Assert.Equal([ComplianceWarning.CourseMissing], Evaluate(Compliant with { TrainingCompletedOn = null }));

    [Fact]
    public void Eighteenth_birthday_today_is_of_age() =>
        Assert.Empty(Evaluate(Compliant with { BirthDate = new DateOnly(2008, 10, 2) }));

    [Fact]
    public void Eighteenth_birthday_tomorrow_is_under_age() =>
        Assert.Equal([ComplianceWarning.UnderAge], Evaluate(Compliant with { BirthDate = new DateOnly(2008, 10, 3) }));

    [Fact]
    public void Born_on_29_February_comes_of_age_on_28_February()
    {
        var facts = Compliant with { BirthDate = new DateOnly(2008, 2, 29) };

        Assert.Equal([ComplianceWarning.UnderAge], Rules.Evaluate(facts, new DateOnly(2026, 2, 27)));
        Assert.Empty(Rules.Evaluate(facts, new DateOnly(2026, 2, 28)));
    }

    [Fact]
    public void No_id_photo() =>
        Assert.Equal([ComplianceWarning.IdPhotoMissing], Evaluate(Compliant with { HasIdPhoto = false }));

    [Fact]
    public void Warnings_come_in_rule_order()
    {
        var facts = new ComplianceFacts(
            BirthDate: new DateOnly(2010, 1, 1),
            License: new ComplianceLicense.Issued(new DateOnly(2027, 1, 1), HasFrontPhoto: false, HasBackPhoto: false),
            TrainingCompletedOn: null,
            HasIdPhoto: false);

        Assert.Equal(
            [
                ComplianceWarning.LicenseExpiring,
                ComplianceWarning.CourseMissing,
                ComplianceWarning.UnderAge,
                ComplianceWarning.IdPhotoMissing,
                ComplianceWarning.LicensePhotosMissing,
            ],
            Evaluate(facts));
    }

    [Fact]
    public void Enum_order_is_the_rule_order() =>
        Assert.Equal(
            [
                ComplianceWarning.LicenseMissing,
                ComplianceWarning.LicensePending,
                ComplianceWarning.LicenseExpired,
                ComplianceWarning.LicenseExpiring,
                ComplianceWarning.CourseMissing,
                ComplianceWarning.UnderAge,
                ComplianceWarning.IdPhotoMissing,
                ComplianceWarning.LicensePhotosMissing,
            ],
            Enum.GetValues<ComplianceWarning>());

    [Theory]
    [InlineData("2000-10-02", "2026-10-02", 26)]
    [InlineData("2000-10-03", "2026-10-02", 25)]
    [InlineData("2026-10-02", "2026-10-02", 0)]
    [InlineData("2008-02-29", "2026-02-28", 18)]
    [InlineData("2008-02-29", "2028-02-29", 20)]
    [InlineData("2008-03-01", "2026-02-28", 17)]
    public void Age_counts_whole_years(string birthDate, string on, int expected) =>
        Assert.Equal(expected, Rules.AgeOn(DateOnly.Parse(birthDate, CultureInfo.InvariantCulture), DateOnly.Parse(on, CultureInfo.InvariantCulture)));

    [Fact]
    public void Codes_are_the_spec_codes() =>
        Assert.Equal(
            ["LICENSE_MISSING", "LICENSE_PENDING", "LICENSE_EXPIRED", "LICENSE_EXPIRING", "COURSE_MISSING", "UNDER_AGE", "ID_PHOTO_MISSING", "LICENSE_PHOTOS_MISSING"],
            EnumCodes.All<ComplianceWarning>());

    /// <summary>
    /// The license warnings and the registry's license status agree on every day around the expiry
    /// (review finding: the comparison lives in two places).
    /// </summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public void License_warnings_agree_with_the_license_status(int daysFromExpiry)
    {
        var expiresOn = new DateOnly(2027, 3, 10);
        var today = expiresOn.AddDays(daysFromExpiry);
        var status = new License(LicenseType.Ae, Pending: false, IssuedOn: new DateOnly(2022, 3, 10), ExpiresOn: expiresOn).StatusOn(today);

        var warnings = Rules.Evaluate(WithExpiry(expiresOn), today);

        Assert.Equal(status == LicenseStatus.Expired, warnings.Contains(ComplianceWarning.LicenseExpired));
        Assert.Equal(status == LicenseStatus.Valid, warnings.Contains(ComplianceWarning.LicenseExpiring));
    }

    [Fact]
    public void Pending_license_status_agrees_with_the_pending_warning()
    {
        var status = new License(LicenseType.Ae, Pending: true, IssuedOn: null, ExpiresOn: null).StatusOn(Today);

        Assert.Equal(LicenseStatus.Pending, status);
        Assert.Contains(ComplianceWarning.LicensePending, Evaluate(Compliant with { License = new ComplianceLicense.Pending() }));
    }

    [Fact]
    public void Facts_print_no_personal_value() =>
        Assert.DoesNotContain("1996", Compliant.ToString(), StringComparison.Ordinal);

    [Fact]
    public void Null_facts_are_rejected() =>
        Assert.Throws<ArgumentNullException>(() => Rules.Evaluate(null!, Today));
}
