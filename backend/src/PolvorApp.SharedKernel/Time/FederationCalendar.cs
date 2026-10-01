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
    public static DateOnly Today(TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);
        var local = TimeZoneInfo.ConvertTime(time.GetUtcNow(), Zone);
        return DateOnly.FromDateTime(local.DateTime);
    }
}
