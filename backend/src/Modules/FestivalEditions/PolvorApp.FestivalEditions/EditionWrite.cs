using PolvorApp.FestivalEditions.Editions;

namespace PolvorApp.FestivalEditions;

/// <summary>
/// The result of an edition write: its outcome, the edition (or milestone) when it was done, and
/// what the problem response needs otherwise (invalid fields, or <c>missing</c> /
/// <c>inProgressYear</c> extensions).
/// </summary>
internal sealed record EditionWrite(
    EditionOutcome Outcome,
    FestivalEdition? Edition = null,
    IReadOnlyDictionary<string, string>? Errors = null,
    IReadOnlyDictionary<string, object?>? Extra = null,
    CalendarMilestone? Milestone = null)
{
    public static EditionWrite Done(FestivalEdition edition) => new(EditionOutcome.Done, edition);

    public static EditionWrite Done(CalendarMilestone milestone) => new(EditionOutcome.Done, Milestone: milestone);

    public static EditionWrite Failed(EditionOutcome outcome, IReadOnlyDictionary<string, object?>? extra = null) => new(outcome, Extra: extra);

    public static EditionWrite Invalid(IReadOnlyDictionary<string, string> errors) => new(EditionOutcome.Invalid, Errors: errors);
}
