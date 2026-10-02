namespace PolvorApp.FestivalEditions.Editions;

/// <summary>An administrative date of an edition (glossary: <c>CalendarMilestone</c>; spec: Calendar milestones).</summary>
internal sealed class CalendarMilestone
{
    public const int TitleMaxLength = 100;

    /// <summary>Most milestones an edition can have (blocking).</summary>
    public const int MaxPerEdition = 50;

    public required Guid Id { get; init; }

    public required Guid EditionId { get; init; }

    public required DateOnly Date { get; set; }

    public required string Title { get; set; }

    public required DateTimeOffset CreatedAt { get; init; }
}
