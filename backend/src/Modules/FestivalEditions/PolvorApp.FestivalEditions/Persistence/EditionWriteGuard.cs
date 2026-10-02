using Microsoft.Extensions.Logging;
using PolvorApp.IdentityAccess.Contracts;

namespace PolvorApp.FestivalEditions.Persistence;

/// <summary>
/// Runs an edition write (design D3), as the registry's write guard does: a lock timeout or deadlock
/// becomes the retryable <see cref="EditionOutcome.Busy"/>, and every rejection is logged at Warning
/// with the operation, outcome and ids only, so refusals and 503 storms are visible to operators.
/// </summary>
internal sealed partial class EditionWriteGuard(FestivalEditionsDbContext db, ICurrentUser currentUser, ILogger<EditionWriteGuard> logger)
{
    public async Task<EditionWrite> RunAsync(string operation, Guid? editionId, Func<Task<EditionWrite>> write)
    {
        ArgumentNullException.ThrowIfNull(write);
        EditionWrite result;
        try
        {
            result = await write();
        }
        catch (Exception exception) when (EditionProblems.IsRetryable(exception))
        {
            db.ChangeTracker.Clear();
            result = EditionWrite.Failed(EditionOutcome.Busy);
        }

        if (result.Outcome != EditionOutcome.Done)
        {
            LogRejected(logger, operation, result.Outcome, editionId, currentUser.UserId);
        }

        return result;
    }

    /// <summary>Logs that a write lost a race on a database constraint (ids and constraint name only).</summary>
    public void LostRace(Guid? editionId, string constraint) => LogLostRace(logger, editionId, constraint);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Edition {Operation} rejected with {Outcome} for edition {EditionId} by user {UserId}")]
    private static partial void LogRejected(ILogger logger, string operation, EditionOutcome outcome, Guid? editionId, Guid? userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Edition write for {EditionId} lost a race on {Constraint}")]
    private static partial void LogLostRace(ILogger logger, Guid? editionId, string constraint);
}
