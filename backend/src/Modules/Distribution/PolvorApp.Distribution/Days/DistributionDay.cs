using PolvorApp.Distribution.Contracts;

namespace PolvorApp.Distribution.Days;

/// <summary>
/// A distribution day (glossary: <c>Distribution</c>; spec: Distribution days (UC-18)): the powder or the
/// rented weapons handed out on one date at one place, in a slot per comparsa. Named <c>DistributionDay</c>
/// because <c>Distribution</c> is the module's namespace.
/// </summary>
internal sealed class DistributionDay
{
    public const int LocationMaxLength = 200;

    public required Guid Id { get; init; }

    public required Guid EditionId { get; init; }

    public required DistributionType Type { get; init; }

    public required DateOnly Date { get; set; }

    public required string Location { get; set; }

    public required DateTimeOffset UpdatedAt { get; set; }

    /// <summary>PostgreSQL <c>xmin</c>: edits and slot changes carry it (README: optimistic concurrency).</summary>
    public uint Version { get; init; }

    public List<DistributionSlot> Slots { get; init; } = [];
}

/// <summary>A comparsa's slot on a distribution day (glossary: <c>DistributionSlot</c>): its start time on the day's date.</summary>
internal sealed class DistributionSlot
{
    public required Guid DistributionId { get; init; }

    public required Guid ComparsaId { get; init; }

    public required TimeOnly StartsAt { get; set; }
}
