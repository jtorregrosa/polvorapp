using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Npgsql;
using PolvorApp.SharedKernel.Http;

namespace PolvorApp.Distribution;

/// <summary>How a distribution operation ended (design D4). Every value but <see cref="Done"/> is blocking.</summary>
internal enum DistributionOutcome
{
    Done,

    /// <summary>A field broke a rule; <see cref="DistributionResult{T}.Errors"/> names it.</summary>
    Invalid,

    /// <summary>The edition or the day does not exist, or is not visible to the user (BR-12).</summary>
    NotFound,
    AlreadyPlanned,
    EditionNotInProgress,
    Modified,
    ProxyNotFound,
    AlreadyAuthorised,
    ProxyAbsent,
    HolderIsProxy,
    NotApplicable,
    LicenseInvalid,

    /// <summary>A lock wait timed out or a deadlock was broken; retryable.</summary>
    Busy,
}

/// <summary>The result of a distribution operation: its value when done, or why not.</summary>
internal sealed record DistributionResult<T>(DistributionOutcome Outcome, T? Value, IReadOnlyDictionary<string, string>? Errors = null)
{
    public static DistributionResult<T> Done(T value) => new(DistributionOutcome.Done, value);

    public static DistributionResult<T> Failed(DistributionOutcome outcome) => new(outcome, default);

    public static DistributionResult<T> Invalid(IReadOnlyDictionary<string, string> errors) => new(DistributionOutcome.Invalid, default, errors);

    public ProblemHttpResult Problem() => DistributionProblems.From(Outcome, Errors);
}

/// <summary>Problem codes of the distribution module, translated by the UI as <c>distribution:errors.&lt;code&gt;</c>.</summary>
internal static class DistributionProblems
{
    public const string NotFound = "distribution.notFound";
    public const string AlreadyPlanned = "distribution.alreadyPlanned";
    public const string EditionNotInProgress = "distribution.editionNotInProgress";
    public const string Modified = "distribution.modified";
    public const string Busy = "distribution.busy";
    public const string ProxyNotFound = "proxies.notFound";
    public const string AlreadyAuthorised = "proxies.alreadyAuthorised";
    public const string ProxyAbsent = "proxies.proxyAbsent";
    public const string HolderIsProxy = "proxies.holderIsProxy";
    public const string NotApplicable = "proxies.notApplicable";
    public const string LicenseInvalid = "proxies.licenseInvalid";
    public const string AuditUnavailable = "distribution.auditUnavailable";

    /// <summary>A text of a PDF list or form, e.g. a name, holds a letter the embedded fonts cannot draw.</summary>
    public const string TextUnprintable = "distribution.textUnprintable";

    public static ProblemHttpResult From(DistributionOutcome outcome, IReadOnlyDictionary<string, string>? errors = null) => outcome switch
    {
        DistributionOutcome.Invalid => ProblemResults.Invalid(errors ?? new Dictionary<string, string>()),
        DistributionOutcome.NotFound => ProblemResults.NotFound(NotFound),
        DistributionOutcome.AlreadyPlanned => ProblemResults.Conflict(AlreadyPlanned),
        DistributionOutcome.EditionNotInProgress => ProblemResults.Conflict(EditionNotInProgress),
        DistributionOutcome.Modified => ProblemResults.Conflict(Modified),
        DistributionOutcome.ProxyNotFound => ProblemResults.NotFound(ProxyNotFound),
        DistributionOutcome.AlreadyAuthorised => ProblemResults.Conflict(AlreadyAuthorised),
        DistributionOutcome.ProxyAbsent => ProblemResults.Conflict(ProxyAbsent),
        DistributionOutcome.HolderIsProxy => ProblemResults.Conflict(HolderIsProxy),
        DistributionOutcome.NotApplicable => ProblemResults.Conflict(NotApplicable),
        DistributionOutcome.LicenseInvalid => ProblemResults.Conflict(LicenseInvalid),
        DistributionOutcome.Busy => ProblemResults.Problem(StatusCodes.Status503ServiceUnavailable, Busy),
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Not a problem outcome."),
    };

    /// <summary>Whether saving failed on the named constraint with this SQL state: a blocking rule lost a race.</summary>
    public static bool Violates(Exception exception, string sqlState, string constraint) =>
        exception.InnerException is PostgresException postgres && postgres.SqlState == sqlState && postgres.ConstraintName == constraint;

    /// <summary>The SQL state of the first database error in the chain, for logs.</summary>
    public static string? SqlState(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres)
            {
                return postgres.SqlState;
            }
        }

        return null;
    }

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

/// <summary>Reasons of field errors besides the shared <c>required</c>, <c>invalid</c> and <c>tooLong</c>.</summary>
internal static class DistributionFieldErrors
{
    /// <summary>A date outside the edition's year or after its festival.</summary>
    public const string OutOfEdition = "outOfEdition";

    /// <summary>A comparsa that does not exist.</summary>
    public const string Unknown = "unknown";

    /// <summary>A comparsa named twice in one set of slots.</summary>
    public const string Duplicate = "duplicate";
}
