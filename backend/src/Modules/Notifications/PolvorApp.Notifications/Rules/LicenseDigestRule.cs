using PolvorApp.ArquebusierRegistry.Contracts;

namespace PolvorApp.Notifications.Rules;

/// <summary>What the license digest counts for one comparsa (spec: License digest (UC-23, BR-04)).</summary>
/// <param name="ComparsaId">The comparsa.</param>
/// <param name="Missing"><c>ACTIVE</c> arquebusiers without a license.</param>
/// <param name="Pending"><c>ACTIVE</c> arquebusiers whose license is pending.</param>
/// <param name="Expired"><c>ACTIVE</c> arquebusiers whose license expired before the digest date.</param>
/// <param name="ExpiringSoon"><c>ACTIVE</c> arquebusiers whose license is valid but expires within 90 days.</param>
internal sealed record LicenseDigestCounts(Guid ComparsaId, int Missing, int Pending, int Expired, int ExpiringSoon)
{
    public bool IsEmpty => Missing + Pending + Expired + ExpiringSoon == 0;
}

/// <summary>
/// The license digest rule (design D8): the same comparisons as the compliance warnings (valid through
/// the expiry day, dates in Europe/Madrid), with a 90-day window instead of 12 months, for
/// <c>ACTIVE</c> arquebusiers only. A reminder: it never blocks anything (BR-04).
/// </summary>
internal static class LicenseDigestRule
{
    public const int ExpiringWithinDays = 90;

    /// <summary>The counts of each comparsa with something to count, by comparsa id; none when nothing is to be reported.</summary>
    public static IReadOnlyList<LicenseDigestCounts> Count(IEnumerable<ArquebusierFacts> arquebusiers, DateOnly date)
    {
        ArgumentNullException.ThrowIfNull(arquebusiers);
        var soon = date.AddDays(ExpiringWithinDays);
        return arquebusiers
            .Where(a => a.Status == ArquebusierStatus.Active)
            .GroupBy(a => a.ComparsaId)
            .Select(comparsa => new LicenseDigestCounts(
                comparsa.Key,
                Missing: comparsa.Count(a => a.License is null),
                Pending: comparsa.Count(a => a.License is ArquebusierLicenseFacts.Pending),
                Expired: comparsa.Count(a => a.License is ArquebusierLicenseFacts.Issued issued && issued.ExpiresOn < date),
                ExpiringSoon: comparsa.Count(a => a.License is ArquebusierLicenseFacts.Issued issued && issued.ExpiresOn >= date && issued.ExpiresOn < soon)))
            .Where(counts => !counts.IsEmpty)
            .OrderBy(counts => counts.ComparsaId)
            .ToList();
    }
}
