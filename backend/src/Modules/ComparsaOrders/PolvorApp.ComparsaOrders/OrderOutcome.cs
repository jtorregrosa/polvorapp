using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PolvorApp.SharedKernel.Http;

namespace PolvorApp.ComparsaOrders;

/// <summary>How an order operation ended (design D6, D8). Every value but <see cref="Done"/> is blocking.</summary>
internal enum OrderOutcome
{
    Done,

    /// <summary>A field rule failed; the fields are named (400).</summary>
    Invalid,
    NotFound,
    AlreadyPrepared,
    EditionNotStarted,
    ComparsaInactive,
    Closed,
    Validated,
    InvalidTransition,
    Modified,
    EntriesInvalid,
    AlreadyInEdition,
    EntryNotFound,
    EntryModified,

    /// <summary>The entry, or the lender of its loan, was erased on a GDPR request: it can no longer be edited (409).</summary>
    EntryErased,

    /// <summary>A row lock waited too long, a deadlock, or a lost race on an entry index; retryable.</summary>
    Busy,
}

/// <summary>Problem codes of the orders, translated by the UI as <c>orders:errors.&lt;code&gt;</c>.</summary>
internal static class OrderProblems
{
    public const string NotFound = "orders.notFound";
    public const string AlreadyPrepared = "orders.alreadyPrepared";
    public const string EditionNotStarted = "orders.editionNotStarted";
    public const string ComparsaInactive = "orders.comparsaInactive";
    public const string Closed = "orders.closed";
    public const string Validated = "orders.validated";
    public const string InvalidTransition = "orders.invalidTransition";
    public const string Modified = "orders.modified";
    public const string EntriesInvalid = "orders.entriesInvalid";
    public const string AlreadyInEdition = "orders.alreadyInEdition";
    public const string EntryNotFound = "entries.notFound";
    public const string EntryModified = "entries.modified";
    public const string EntryErased = "orders.entryErased";
    public const string Busy = "orders.busy";

    /// <summary>The problem for <paramref name="outcome"/>; <paramref name="extra"/> carries e.g. the invalid <c>entries</c>.</summary>
    public static ProblemHttpResult From(OrderOutcome outcome, IReadOnlyDictionary<string, object?>? extra = null) => outcome switch
    {
        OrderOutcome.NotFound => ProblemResults.NotFound(NotFound),
        OrderOutcome.AlreadyPrepared => ProblemResults.Conflict(AlreadyPrepared),
        OrderOutcome.EditionNotStarted => ProblemResults.Conflict(EditionNotStarted),
        OrderOutcome.ComparsaInactive => ProblemResults.Conflict(ComparsaInactive),
        OrderOutcome.Closed => ProblemResults.Conflict(Closed),
        OrderOutcome.Validated => ProblemResults.Conflict(Validated),
        OrderOutcome.InvalidTransition => ProblemResults.Conflict(InvalidTransition),
        OrderOutcome.Modified => ProblemResults.Conflict(Modified),
        OrderOutcome.EntriesInvalid => ProblemResults.Problem(StatusCodes.Status409Conflict, EntriesInvalid, extra),
        OrderOutcome.AlreadyInEdition => ProblemResults.Conflict(AlreadyInEdition),
        OrderOutcome.EntryNotFound => ProblemResults.NotFound(EntryNotFound),
        OrderOutcome.EntryModified => ProblemResults.Conflict(EntryModified),
        OrderOutcome.EntryErased => ProblemResults.Conflict(EntryErased),
        OrderOutcome.Busy => ProblemResults.Problem(StatusCodes.Status503ServiceUnavailable, Busy),
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Not a problem outcome."),
    };

    /// <summary>Whether saving failed on the named unique index: a blocking rule lost a race.</summary>
    public static bool IsUniqueViolation(DbUpdateException exception, string indexName) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres
        && postgres.ConstraintName == indexName;

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
