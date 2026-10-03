using Microsoft.Extensions.Logging;
using PolvorApp.IdentityAccess.Contracts;

namespace PolvorApp.Distribution.Persistence;

/// <summary>
/// Runs a distribution write (design D4), as the other modules' write guards do: a lock timeout or
/// deadlock becomes the retryable <see cref="DistributionOutcome.Busy"/>, and every rejection is logged
/// at Warning with the operation, outcome and ids only (never personal data).
/// </summary>
internal sealed partial class DistributionWriteGuard(DistributionDbContext db, ICurrentUser currentUser, ILogger<DistributionWriteGuard> logger)
{
    public async Task<DistributionResult<T>> RunAsync<T>(string operation, Guid? targetId, Func<Task<DistributionResult<T>>> write)
    {
        ArgumentNullException.ThrowIfNull(write);
        DistributionResult<T> result;
        try
        {
            result = await write();
        }
        catch (Exception exception) when (DistributionProblems.IsRetryable(exception))
        {
            // The cause (lock timeout or deadlock) by SQL state only: never the statement or its values.
            LogBusy(logger, operation, DistributionProblems.SqlState(exception), targetId);
            db.ChangeTracker.Clear();
            result = DistributionResult<T>.Failed(DistributionOutcome.Busy);
        }

        if (result.Outcome != DistributionOutcome.Done)
        {
            LogRejected(logger, operation, result.Outcome, targetId, currentUser.UserId);
        }

        return result;
    }

    /// <summary>Logs a request refused before its write started (scope, edition rule, validation).</summary>
    public void Rejected(string operation, DistributionOutcome outcome, Guid? targetId) => LogRejected(logger, operation, outcome, targetId, currentUser.UserId);

    /// <summary>Logs data that should not be missing (e.g. a linked arquebusier the registry no longer returns), by id only.</summary>
    public void Inconsistent(string what, Guid? targetId) => LogInconsistent(logger, what, targetId);

    /// <summary>Logs that a write lost a race on a database constraint (ids and constraint name only).</summary>
    public void LostRace(Guid? targetId, string constraint) => LogLostRace(logger, targetId, constraint);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Distribution {Operation} rejected with {Outcome} for {TargetId} by user {UserId}")]
    private static partial void LogRejected(ILogger logger, string operation, DistributionOutcome outcome, Guid? targetId, Guid? userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Distribution {Operation} for {TargetId} was busy ({SqlState})")]
    private static partial void LogBusy(ILogger logger, string operation, string? sqlState, Guid? targetId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Distribution data inconsistent: {What} for {TargetId}")]
    private static partial void LogInconsistent(ILogger logger, string what, Guid? targetId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Distribution write for {TargetId} lost a race on {Constraint}")]
    private static partial void LogLostRace(ILogger logger, Guid? targetId, string constraint);
}
