using System.Globalization;
using Microsoft.Extensions.Time.Testing;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.FestivalEditions.Contracts;
using PolvorApp.Notifications.Rules;

namespace PolvorApp.Api.Tests.Notifications;

/// <summary>The pure rules of the notifications module (design D5, D8–D10).</summary>
public sealed class NotificationRulesTests
{
    private static readonly DateOnly DigestDate = new(2031, 1, 1);
    private static readonly Guid North = Guid.CreateVersion7();
    private static readonly Guid South = Guid.CreateVersion7();

    [Fact]
    public void The_digest_counts_each_license_problem_of_active_arquebusiers_per_comparsa()
    {
        var counts = LicenseDigestRule.Count(
            [
                Facts(North, license: null),
                Facts(North, new ArquebusierLicenseFacts.Pending(LicenseType.Ae)),
                Facts(North, Issued(DigestDate.AddDays(-1))),
                Facts(North, Issued(DigestDate)),
                Facts(North, Issued(DigestDate.AddDays(89))),
                Facts(North, Issued(DigestDate.AddDays(90))),
                Facts(North, Issued(DigestDate.AddDays(-30)), ArquebusierStatus.Reserve),
                Facts(South, license: null, ArquebusierStatus.Reserve),
                Facts(South, Issued(DigestDate.AddYears(2))),
            ],
            DigestDate);

        Assert.Equal([new LicenseDigestCounts(North, Missing: 1, Pending: 1, Expired: 1, ExpiringSoon: 2)], counts);
    }

    [Fact]
    public void Nothing_to_report_gives_no_counts()
    {
        Assert.Empty(LicenseDigestRule.Count([Facts(North, Issued(DigestDate.AddYears(1)))], DigestDate));
        Assert.Empty(LicenseDigestRule.Count([], DigestDate));
    }

    [Theory]
    [InlineData(8, 7, null)]
    [InlineData(7, 7, "Week")]
    [InlineData(2, 7, "Week")]
    [InlineData(1, 7, "LastDays")]
    [InlineData(0, 7, "LastDays")]
    [InlineData(-1, 7, null)]
    [InlineData(10, 10, "Week")]
    [InlineData(11, 10, null)]
    [InlineData(2, 2, "Week")]
    [InlineData(3, 2, null)]
    [InlineData(14, 14, "Week")]
    public void The_close_reminder_depends_on_the_days_left_and_the_lead_time(int daysLeft, int leadDays, string? expected)
    {
        var today = new DateOnly(2031, 2, 3);

        Assert.Equal(expected, Schedule.CloseReminderFor(today, today.AddDays(daysLeft), leadDays)?.ToString());
    }

    [Theory]
    [InlineData(7, "2030-11-23", "2030-11-30")]
    [InlineData(3, "2030-11-23", "2030-11-26")]
    [InlineData(1, "2030-11-23", "2030-11-24")]
    [InlineData(14, "2030-11-23", "2030-12-07")]
    public void The_milestone_window_runs_from_today_to_the_lead_time_ahead(int leadDays, string from, string to)
    {
        var window = Schedule.MilestoneWindow(new DateOnly(2030, 11, 23), leadDays);

        Assert.Equal((DateOnly.Parse(from, CultureInfo.InvariantCulture), DateOnly.Parse(to, CultureInfo.InvariantCulture)), window);
    }

    [Fact]
    public void Firing_chiefs_are_reminded_of_milestones_of_the_edition_in_progress_only()
    {
        Assert.True(Schedule.RemindsFiringChiefs(EditionStatus.InProgress));
        Assert.False(Schedule.RemindsFiringChiefs(EditionStatus.Draft));
        Assert.False(Schedule.RemindsFiringChiefs(EditionStatus.Closed));
    }

    [Theory]
    [InlineData("2031-01-01T06:59:00Z", false)] // 07:59 in winter (UTC+1)
    [InlineData("2031-01-01T07:00:00Z", true)] // 08:00 in winter
    [InlineData("2031-07-01T05:59:00Z", false)] // 07:59 in summer (UTC+2)
    [InlineData("2031-07-01T06:00:00Z", true)] // 08:00 in summer
    [InlineData("2031-03-30T05:59:00Z", false)] // the spring-forward day: 07:59 CEST
    [InlineData("2031-03-30T06:00:00Z", true)]
    [InlineData("2031-10-26T06:59:00Z", false)] // the fall-back day: 07:59 CET
    [InlineData("2031-10-26T07:00:00Z", true)]
    public void Scheduled_emails_wait_until_eight_in_madrid(string utc, bool may)
    {
        var time = new FakeTimeProvider(DateTimeOffset.Parse(utc, System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(may, Schedule.MaySendScheduled(time));
    }

    [Fact]
    public void Topics_change_with_a_moved_date()
    {
        var milestone = Guid.CreateVersion7();
        var edition = Guid.CreateVersion7();

        Assert.NotEqual(Topics.Milestone(milestone, new DateOnly(2030, 11, 30)), Topics.Milestone(milestone, new DateOnly(2030, 12, 14)));
        Assert.NotEqual(
            Topics.Close(CloseReminder.Week, edition, North, new DateOnly(2031, 2, 10)),
            Topics.Close(CloseReminder.Week, edition, North, new DateOnly(2031, 2, 17)));
        Assert.NotEqual(
            Topics.Close(CloseReminder.Week, edition, North, new DateOnly(2031, 2, 10)),
            Topics.Close(CloseReminder.LastDays, edition, North, new DateOnly(2031, 2, 10)));
        Assert.Equal("digest:2031-03", Topics.Digest(new DateOnly(2031, 3, 2)));
    }

    [Fact]
    public void Retries_grow_and_stop_after_a_day()
    {
        var created = new DateTimeOffset(2031, 1, 1, 8, 0, 0, TimeSpan.Zero);

        Assert.Equal(created.AddMinutes(1), RetrySchedule.Next(1, created, created));
        Assert.Equal(created.AddMinutes(10), RetrySchedule.Next(2, created, created.AddMinutes(5)));
        Assert.Equal(created.AddMinutes(30), RetrySchedule.Next(3, created, created.AddMinutes(15)));
        Assert.Equal(created.AddMinutes(90), RetrySchedule.Next(4, created, created.AddMinutes(30)));
        Assert.Equal(created.AddHours(14), RetrySchedule.Next(9, created, created.AddHours(10)));
        Assert.Null(RetrySchedule.Next(10, created, created.AddHours(24)));
        Assert.Throws<ArgumentOutOfRangeException>(() => RetrySchedule.Next(0, created, created));
    }

    private static ArquebusierFacts Facts(Guid comparsaId, ArquebusierLicenseFacts? license, ArquebusierStatus status = ArquebusierStatus.Active) =>
        new(Guid.CreateVersion7(), comparsaId, status, Gender.Unspecified, new DateOnly(1990, 5, 5), license, null, HasIdPhoto: false, []);

    private static ArquebusierLicenseFacts.Issued Issued(DateOnly expiresOn) => new(LicenseType.Ae, expiresOn, HasFrontPhoto: false, HasBackPhoto: false);
}
