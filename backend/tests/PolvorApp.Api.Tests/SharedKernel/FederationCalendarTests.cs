using System.Globalization;
using Microsoft.Extensions.Time.Testing;
using PolvorApp.SharedKernel.Time;

namespace PolvorApp.Api.Tests.SharedKernel;

/// <summary>"Today" is the Federation's date in Europe/Madrid, not the UTC date (change add-arquebusier-registry, design D3).</summary>
public sealed class FederationCalendarTests
{
    [Theory]
    // Winter (UTC+1): 23:30 UTC is already the next day in Madrid.
    [InlineData("2026-01-14T22:59:59Z", "2026-01-14")]
    [InlineData("2026-01-14T23:30:00Z", "2026-01-15")]
    // Summer (UTC+2): 22:00 UTC is already the next day in Madrid.
    [InlineData("2026-07-14T21:59:59Z", "2026-07-14")]
    [InlineData("2026-07-14T22:00:00Z", "2026-07-15")]
    // The days the clocks change: forward on 2026-03-29, back on 2026-10-25.
    [InlineData("2026-03-28T23:30:00Z", "2026-03-29")]
    [InlineData("2026-10-24T22:30:00Z", "2026-10-25")]
    // Last day of the year rolls over in Madrid before it does in UTC.
    [InlineData("2026-12-31T23:00:00Z", "2027-01-01")]
    public void Today_is_the_date_in_Madrid(string utcNow, string expected)
    {
        var time = new FakeTimeProvider(DateTimeOffset.Parse(utcNow, CultureInfo.InvariantCulture));

        Assert.Equal(DateOnly.Parse(expected, CultureInfo.InvariantCulture), FederationCalendar.Today(time));
    }
}
