using System.Globalization;
using PolvorApp.FestivalEditions.Contracts;
using PolvorApp.SharedKernel.Time;

namespace PolvorApp.Notifications.Rules;

/// <summary>Which planned close reminder is due (spec: Planned close reminders (BR-10)).</summary>
internal enum CloseReminder
{
    /// <summary>The close date is 2 days up to the settings' first close reminder lead time ahead (7 by default).</summary>
    Week,

    /// <summary>The close date is tomorrow or today.</summary>
    LastDays,
}

/// <summary>
/// When the scheduled notifications are due (design D5, D9, D10). Pure functions of the date, so they
/// are tested with any date; callers pass <see cref="FederationCalendar.Today"/>.
/// </summary>
internal static class Schedule
{
    /// <summary>Scheduled emails are not sent before 08:00 Europe/Madrid (spec: Scheduled notifications).</summary>
    public const int SendFromHour = 8;

    /// <summary>The first close reminder is never sent later than this many days before the close.</summary>
    private const int WeekReminderFirstDay = 2;

    /// <summary>Whether a scheduled run may send now: from 08:00 local time.</summary>
    public static bool MaySendScheduled(TimeProvider time) => MaySendScheduled(FederationCalendar.Now(time));

    /// <summary>Whether a scheduled run may send at <paramref name="madridTime"/> (Europe/Madrid).</summary>
    public static bool MaySendScheduled(DateTimeOffset madridTime) => madridTime.Hour >= SendFromHour;

    /// <summary>
    /// The reminder due for a planned close date, or null when none is. <paramref name="leadDays"/> is the
    /// first close reminder lead time of the Federation settings (add-federation-settings).
    /// </summary>
    public static CloseReminder? CloseReminderFor(DateOnly today, DateOnly closeOn, int leadDays)
    {
        var days = closeOn.DayNumber - today.DayNumber;
        return days switch
        {
            _ when days >= WeekReminderFirstDay && days <= leadDays => CloseReminder.Week,
            0 or 1 => CloseReminder.LastDays,
            _ => null,
        };
    }

    /// <summary>
    /// Whether a milestone dated <paramref name="date"/> is due for its reminder today, from
    /// <paramref name="leadDays"/> days before (the Federation settings) until the date itself.
    /// </summary>
    public static bool IsMilestoneDue(DateOnly today, DateOnly date, int leadDays)
    {
        var days = date.DayNumber - today.DayNumber;
        return days >= 0 && days <= leadDays;
    }

    /// <summary>Whether FiringChiefs may be reminded of a milestone of an edition in that status (BR-12: they cannot see a draft).</summary>
    public static bool RemindsFiringChiefs(EditionStatus status) => status == EditionStatus.InProgress;
}

/// <summary>
/// The deduplication keys of the deliveries (design D3). A moved date makes a new key, so its reminder
/// is due again; one key per recipient is sent once.
/// </summary>
internal static class Topics
{
    public static string Event(Guid eventId) => $"event:{eventId}";

    public static string Digest(DateOnly date) => $"digest:{date.ToString("yyyy-MM", CultureInfo.InvariantCulture)}";

    public static string Close(CloseReminder reminder, Guid editionId, Guid comparsaId, DateOnly closeOn) =>
        $"{(reminder == CloseReminder.Week ? "close7" : "close1")}:{editionId}:{comparsaId}:{Iso(closeOn)}";

    public static string Milestone(Guid milestoneId, DateOnly date) => $"milestone:{milestoneId}:{Iso(date)}";

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

/// <summary>When a failed delivery is tried again (design D5): 1, 5, 15 and 60 minutes, then every 4 hours, for at least 24 hours.</summary>
internal static class RetrySchedule
{
    public static readonly TimeSpan GiveUpAfter = TimeSpan.FromHours(24);

    private static readonly TimeSpan[] Delays =
        [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(60)];

    private static readonly TimeSpan Later = TimeSpan.FromHours(4);

    /// <summary>
    /// The next attempt after the <paramref name="failedAttempts"/>-th failure at <paramref name="now"/>,
    /// or null to give up because the delivery was created more than 24 hours ago.
    /// </summary>
    public static DateTimeOffset? Next(int failedAttempts, DateTimeOffset createdAt, DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(failedAttempts, 1);
        if (now - createdAt >= GiveUpAfter)
        {
            return null;
        }

        return now + (failedAttempts <= Delays.Length ? Delays[failedAttempts - 1] : Later);
    }
}
