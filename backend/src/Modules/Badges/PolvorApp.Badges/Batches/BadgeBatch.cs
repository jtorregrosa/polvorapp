using PolvorApp.Badges.Contracts;
using PolvorApp.Exports.Contracts;
using PolvorApp.SharedKernel.Text;
using PolvorApp.SharedKernel.Validation;

namespace PolvorApp.Badges.Batches;

/// <summary>The body of a badge sheet request: exactly one of a comparsa and a selection, and the labels' language.</summary>
/// <param name="ComparsaId">Every arquebusier of this comparsa.</param>
/// <param name="ArquebusierIds">These arquebusiers, of any comparsa.</param>
/// <param name="Language">The labels' language code: <c>es-ES</c>, <c>ca-ES-valencia</c> or <c>en</c>.</param>
public sealed record BadgeSheetRequest(Guid? ComparsaId, IReadOnlyList<Guid>? ArquebusierIds, string? Language);

internal enum BadgeBatchKind
{
    Comparsa,
    Selection,
}

/// <summary>A valid request: the batch kind, its comparsa or its distinct arquebusiers in request order, and the language.</summary>
internal sealed record ValidBadgeBatch(BadgeBatchKind Kind, Guid? ComparsaId, IReadOnlyList<Guid> ArquebusierIds, BadgeLanguage Language);

/// <summary>The outcome of <see cref="BadgeBatch.Parse"/>: the batch, or the field errors.</summary>
internal sealed record ParsedBadgeBatch(ValidBadgeBatch? Batch, IReadOnlyDictionary<string, string> Errors);

/// <summary>Who a badge is for, with what orders the sheet.</summary>
internal sealed record BadgeSubject(Guid ArquebusierId, string ComparsaName, string LastName, string FirstName);

/// <summary>
/// The rules of a badge sheet request (spec: Badge batches, Badge language; design D4), reported all at
/// once, and the print order. Pure: whether the comparsa and the arquebusiers exist is checked after.
/// </summary>
internal static class BadgeBatch
{
    /// <summary>Field reason of a selection beyond <see cref="DocumentBadgeSheet.MaxBadges"/> distinct arquebusiers.</summary>
    public const string TooManyReason = "tooMany";

    public static ParsedBadgeBatch Parse(BadgeSheetRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        if (request.ComparsaId is not null && request.ArquebusierIds is not null)
        {
            errors["batch"] = InputFields.Invalid;
        }
        else if (request.ComparsaId is null && request.ArquebusierIds is null)
        {
            errors["batch"] = InputFields.Required;
        }

        var language = InputFields.RequiredCode<BadgeLanguage>(request.Language, "language", errors);

        IReadOnlyList<Guid> ids = request.ArquebusierIds is { } requested ? [.. requested.Distinct()] : [];
        if (request.ArquebusierIds is not null && request.ComparsaId is null)
        {
            if (ids.Count == 0)
            {
                errors["arquebusierIds"] = InputFields.Required;
            }
            else if (ids.Count > DocumentBadgeSheet.MaxBadges)
            {
                errors["arquebusierIds"] = TooManyReason;
            }
        }

        if (errors.Count > 0 || language is not { } chosen)
        {
            return new ParsedBadgeBatch(null, errors);
        }

        var batch = request.ComparsaId is { } comparsaId
            ? new ValidBadgeBatch(BadgeBatchKind.Comparsa, comparsaId, [], chosen)
            : new ValidBadgeBatch(BadgeBatchKind.Selection, null, ids, chosen);
        return new ParsedBadgeBatch(batch, errors);
    }

    /// <summary>
    /// By comparsa name, then surname, then name, in Spanish order (spec: Badge batches); namesakes by id,
    /// so the same data always gives the same document.
    /// </summary>
    public static IReadOnlyList<BadgeSubject> Order(IEnumerable<BadgeSubject> subjects) =>
    [
        .. subjects
            .OrderBy(s => s.ComparsaName, SpanishOrder.Names)
            .ThenBy(s => s.LastName, SpanishOrder.Names)
            .ThenBy(s => s.FirstName, SpanishOrder.Names)
            .ThenBy(s => s.ArquebusierId),
    ];
}
