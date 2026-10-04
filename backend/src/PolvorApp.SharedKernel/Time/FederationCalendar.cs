namespace PolvorApp.SharedKernel.Time;

/// <summary>
/// The Federation's calendar date. Rules such as "not in the future" or "expired" compare dates in
/// Europe/Madrid, where the festival takes place, never the UTC date (change add-arquebusier-registry,
/// design D3).
/// </summary>
public static class FederationCalendar
{
    /// <summary>IANA id: resolved from tzdata on Linux and through ICU on Windows (both need InvariantGlobalization off).</summary>
    public const string TimeZoneId = "Europe/Madrid";

    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);

    /// <summary>Today's date in Europe/Madrid.</summary>
    public static DateOnly Today(TimeProvider time) => DateOnly.FromDateTime(Now(time).DateTime);

    /// <summary>The instant <paramref name="date"/> starts in Europe/Madrid, e.g. a day filter (add-audit-privacy).</summary>
    public static DateTimeOffset StartOf(DateOnly date)
    {
        var utc = TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), Zone);
        return new DateTimeOffset(utc, TimeSpan.Zero);
    }

    /// <summary><paramref name="instant"/> in Europe/Madrid, e.g. to print it (add-audit-privacy).</summary>
    public static DateTimeOffset InMadrid(DateTimeOffset instant) => TimeZoneInfo.ConvertTime(instant, Zone);

    /// <summary>The current time in Europe/Madrid, e.g. to send scheduled emails at a local hour (add-notifications).</summary>
    public static DateTimeOffset Now(TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);
        return TimeZoneInfo.ConvertTime(time.GetUtcNow(), Zone);
    }
}
