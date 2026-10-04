namespace PolvorApp.Notifications.Persistence;

/// <summary>
/// A scheduled job that ran for a period (design D5, D8), e.g. the license digest of 2031-01. The digest
/// is worked out once per month, on the first run of the month, even when nothing was to be reported,
/// so it is never worked out again later in that month.
/// </summary>
internal sealed class NotificationRun
{
    public const int JobMaxLength = 32;
    public const int PeriodMaxLength = 16;

    public const string LicenseDigestJob = "LICENSE_DIGEST";

    public required string Job { get; init; }

    public required string Period { get; init; }

    public required DateTimeOffset RanAt { get; init; }
}
