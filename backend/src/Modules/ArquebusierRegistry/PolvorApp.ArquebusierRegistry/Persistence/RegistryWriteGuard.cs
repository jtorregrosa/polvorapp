using Microsoft.Extensions.Logging;
using PolvorApp.IdentityAccess.Contracts;

namespace PolvorApp.ArquebusierRegistry.Persistence;

/// <summary>
/// Runs a registry write (design D5, D11): a lock timeout (another request held the row too long)
/// or a deadlock becomes the retryable <see cref="RegistryOutcome.Busy"/>, and every rejection is
/// logged at Warning with the operation, outcome and ids only, never personal values, so duplicate
/// probing is visible.
/// </summary>
internal sealed partial class RegistryWriteGuard(ArquebusierRegistryDbContext db, ICurrentUser currentUser, ILogger<RegistryWriteGuard> logger)
{
    public async Task<(RegistryOutcome Outcome, T? Value)> RunAsync<T>(
        string operation, Guid? arquebusierId, Guid? weaponId, Func<Task<(RegistryOutcome Outcome, T? Value)>> write)
        where T : class
    {
        (RegistryOutcome Outcome, T? Value) result;
        try
        {
            result = await write();
        }
        catch (Exception exception) when (RegistryLocks.IsRetryable(exception))
        {
            db.ChangeTracker.Clear();
            result = (RegistryOutcome.Busy, null);
        }

        if (result.Outcome != RegistryOutcome.Done)
        {
            LogRejected(logger, operation, result.Outcome, arquebusierId, weaponId, currentUser.UserId);
        }

        return result;
    }

    /// <summary>Logs that a write lost a race on a database constraint (ids and constraint name only).</summary>
    public void LostRace(Guid targetId, string constraint) => LogLostRace(logger, targetId, constraint);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Registry {Operation} rejected with {Outcome} for arquebusier {ArquebusierId}, weapon {WeaponId}, by user {UserId}")]
    private static partial void LogRejected(ILogger logger, string operation, RegistryOutcome outcome, Guid? arquebusierId, Guid? weaponId, Guid? userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Registry write for {TargetId} lost a race on {Constraint}")]
    private static partial void LogLostRace(ILogger logger, Guid targetId, string constraint);
}
