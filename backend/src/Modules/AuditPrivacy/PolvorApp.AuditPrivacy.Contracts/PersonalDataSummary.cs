namespace PolvorApp.AuditPrivacy.Contracts;

/// <summary>A registered arquebusier, as the lookup shows them (spec: Looking up a person (UC-26)).</summary>
public sealed record RegistryRecordSummary(
    Guid ArquebusierId, string FirstName, string LastName, Guid ComparsaId, string Status, int OwnedWeapons, int Photos)
{
    /// <summary>Never the name: it is personal data.</summary>
    public override string ToString() => $"RegistryRecordSummary({ArquebusierId})";
}

/// <summary>A person's entry in an edition, from the registry link or the entry's copy.</summary>
public sealed record EditionEntrySummary(
    Guid EntryId, Guid EditionId, int EditionYear, string EditionStatus, bool OrdersOpen, Guid ComparsaId, string OrderStatus, bool Erased);

/// <summary>The loans of one edition in which the person is the lender.</summary>
public sealed record LenderLoansSummary(int EditionYear, int Loans);

/// <summary>Count codes of <see cref="PersonalDataSummary.Counts"/> that the audit module reads.</summary>
public static class PersonalDataCounts
{
    /// <summary>Pickup authorisations in which one of the person's entries takes part.</summary>
    public const string PickupProxies = "pickupProxies";
}

/// <summary>
/// What one or more modules hold about a person or a user (design D5). Participants return their part
/// and the audit module merges them.
/// </summary>
public sealed record PersonalDataSummary
{
    public static readonly PersonalDataSummary Empty = new();

    public RegistryRecordSummary? Registry { get; init; }

    public IReadOnlyList<EditionEntrySummary> Entries { get; init; } = [];

    public IReadOnlyList<LenderLoansSummary> LenderLoans { get; init; } = [];

    /// <summary>Other counts by code (e.g. <c>pickupProxies</c>, <c>assignments</c>, <c>notificationDeliveries</c>).</summary>
    public IReadOnlyDictionary<string, int> Counts { get; init; } = new Dictionary<string, int>();

    /// <summary>Whether anything is held.</summary>
    public bool HoldsAnything => Registry is not null || Entries.Count > 0 || LenderLoans.Count > 0 || Counts.Values.Any(c => c > 0);

    public PersonalDataSummary Merge(PersonalDataSummary other)
    {
        ArgumentNullException.ThrowIfNull(other);
        var counts = new Dictionary<string, int>(Counts, StringComparer.Ordinal);
        foreach (var (code, count) in other.Counts)
        {
            counts[code] = counts.GetValueOrDefault(code) + count;
        }

        return new PersonalDataSummary
        {
            Registry = Registry ?? other.Registry,
            Entries = [.. Entries, .. other.Entries],
            LenderLoans = [.. LenderLoans, .. other.LenderLoans],
            Counts = counts,
        };
    }
}
