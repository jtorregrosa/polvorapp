using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PolvorApp.SharedKernel.Http;

namespace PolvorApp.FestivalEditions;

/// <summary>How an edition operation ended (design D3, D4). Every value but <see cref="Done"/> is blocking.</summary>
internal enum EditionOutcome
{
    Done,

    /// <summary>A field rule that depends on the stored edition failed; the fields are named (400).</summary>
    Invalid,
    NotFound,
    YearTaken,
    Modified,
    InvalidTransition,
    AnotherInProgress,
    Incomplete,
    OrdersOpen,
    NotInProgress,
    NotDraft,

    /// <summary>A draft edition that other modules' records reference, e.g. comparsa orders.</summary>
    InUse,
    TooManyMilestones,
    MilestoneNotFound,

    /// <summary>A row lock waited too long, or a deadlock; retryable.</summary>
    Busy,
}

/// <summary>Problem codes of the editions, translated by the UI as <c>editions:errors.&lt;code&gt;</c>.</summary>
internal static class EditionProblems
{
    public const string NotFound = "editions.notFound";
    public const string YearTaken = "editions.yearTaken";
    public const string Modified = "editions.modified";
    public const string InvalidTransition = "editions.invalidTransition";
    public const string AnotherInProgress = "editions.anotherInProgress";
    public const string Incomplete = "editions.incomplete";
    public const string OrdersOpen = "editions.ordersOpen";
    public const string NotInProgress = "editions.notInProgress";
    public const string NotDraft = "editions.notDraft";
    public const string InUse = "editions.inUse";
    public const string TooManyMilestones = "editions.tooManyMilestones";
    public const string MilestoneNotFound = "editions.milestoneNotFound";
    public const string Busy = "editions.busy";

    /// <summary>The problem for a failed write.</summary>
    public static ProblemHttpResult From(EditionWrite write)
    {
        ArgumentNullException.ThrowIfNull(write);
        return write.Outcome == EditionOutcome.Invalid
            ? ProblemResults.Invalid(write.Errors ?? new Dictionary<string, string>())
            : From(write.Outcome, write.Extra);
    }

    /// <summary>The problem for <paramref name="outcome"/>; <paramref name="extra"/> carries <c>missing</c> or <c>inProgressYear</c>.</summary>
    public static ProblemHttpResult From(EditionOutcome outcome, IReadOnlyDictionary<string, object?>? extra = null) => outcome switch
    {
        EditionOutcome.NotFound => ProblemResults.NotFound(NotFound),
        EditionOutcome.YearTaken => ProblemResults.Conflict(YearTaken),
        EditionOutcome.Modified => ProblemResults.Conflict(Modified),
        EditionOutcome.InvalidTransition => ProblemResults.Conflict(InvalidTransition),
        EditionOutcome.AnotherInProgress => ProblemResults.Problem(StatusCodes.Status409Conflict, AnotherInProgress, extra),
        EditionOutcome.Incomplete => ProblemResults.Problem(StatusCodes.Status409Conflict, Incomplete, extra),
        EditionOutcome.OrdersOpen => ProblemResults.Conflict(OrdersOpen),
        EditionOutcome.NotInProgress => ProblemResults.Conflict(NotInProgress),
        EditionOutcome.NotDraft => ProblemResults.Conflict(NotDraft),
        EditionOutcome.InUse => ProblemResults.Conflict(InUse),
        EditionOutcome.TooManyMilestones => ProblemResults.Conflict(TooManyMilestones),
        EditionOutcome.MilestoneNotFound => ProblemResults.NotFound(MilestoneNotFound),
        EditionOutcome.Busy => ProblemResults.Problem(StatusCodes.Status503ServiceUnavailable, Busy),
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Not a problem outcome."),
    };

    /// <summary>Whether saving failed on the named unique index: a blocking rule lost a race.</summary>
    public static bool IsUniqueViolation(DbUpdateException exception, string indexName) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres
        && postgres.ConstraintName == indexName;

    /// <summary>Whether saving failed on the named foreign key, e.g. a catalogue model deleted meanwhile.</summary>
    public static bool IsForeignKeyViolation(DbUpdateException exception, string constraintName) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation } postgres
        && postgres.ConstraintName == constraintName;

    /// <summary>A lock wait that hit the 5 s timeout, or the database aborting one side of a deadlock.</summary>
    public static bool IsRetryable(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException { SqlState: PostgresErrorCodes.LockNotAvailable or PostgresErrorCodes.DeadlockDetected })
            {
                return true;
            }
        }

        return false;
    }
}
