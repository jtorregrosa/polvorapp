using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using PolvorApp.ArquebusierRegistry.Lock;
using PolvorApp.IdentityAccess.Contracts;

namespace PolvorApp.ArquebusierRegistry.Persistence;

/// <summary>
/// Runs a registry write (design D5, D11): a lock timeout (another request held the row too long)
/// or a deadlock becomes the retryable <see cref="RegistryOutcome.Busy"/>, a write refused by the
/// registry lock becomes <see cref="RegistryOutcome.RegistryLocked"/> (add-festival-editions, design
/// D8), and every rejection is logged at Warning with the operation, outcome and ids only, never
/// personal values, so duplicate probing is visible.
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
        catch (RegistryLockedException)
        {
            db.ChangeTracker.Clear();
            result = (RegistryOutcome.RegistryLocked, null);
        }

        if (result.Outcome != RegistryOutcome.Done)
        {
            LogRejected(logger, operation, result.Outcome, arquebusierId, weaponId, currentUser.UserId);
        }

        return result;
    }

    /// <summary>
    /// Starts the transaction of a write a FiringChief can reach (BR-10, design D8). For a FiringChief
    /// it reads the lock <c>FOR SHARE</c> in that transaction: locking the registry (<c>FOR UPDATE</c>)
    /// waits for these writes, and every write that starts after the lock commits sees it, so no
    /// FiringChief write commits after the lock is on. A locked registry ends the write through
    /// <see cref="RunAsync{T}"/> as <see cref="RegistryOutcome.RegistryLocked"/>. Admins always write.
    /// </summary>
    public async Task<IDbContextTransaction> BeginWriteAsync(CancellationToken cancellationToken)
    {
        var transaction = await db.BeginWriteAsync(cancellationToken);
        if (currentUser.IsAdmin)
        {
            return transaction;
        }

        try
        {
            var locked = await db.Database
                .SqlQuery<bool>($"SELECT locked AS \"Value\" FROM registry.registry_settings WHERE id = {RegistrySettings.SingletonId} FOR SHARE")
                .SingleAsync(cancellationToken);
            if (locked)
            {
                throw new RegistryLockedException();
            }

            return transaction;
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// Whether the registry is locked for the caller, read without a lock: a cheap early refusal
    /// (e.g. before processing an image). The check in <see cref="BeginWriteAsync"/> stays the authority.
    /// </summary>
    public async Task<bool> IsLockedForCallerAsync(CancellationToken cancellationToken) =>
        !currentUser.IsAdmin
        && await db.Settings.AsNoTracking()
            .Where(s => s.Id == RegistrySettings.SingletonId)
            .Select(s => s.Locked)
            .SingleAsync(cancellationToken);

    /// <summary>Logs a refusal that never reached <see cref="RunAsync{T}"/>.</summary>
    public void Rejected(string operation, RegistryOutcome outcome, Guid? arquebusierId) =>
        LogRejected(logger, operation, outcome, arquebusierId, null, currentUser.UserId);

    /// <summary>Logs that a write lost a race on a database constraint (ids and constraint name only).</summary>
    public void LostRace(Guid targetId, string constraint) => LogLostRace(logger, targetId, constraint);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Registry {Operation} rejected with {Outcome} for arquebusier {ArquebusierId}, weapon {WeaponId}, by user {UserId}")]
    private static partial void LogRejected(ILogger logger, string operation, RegistryOutcome outcome, Guid? arquebusierId, Guid? weaponId, Guid? userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Registry write for {TargetId} lost a race on {Constraint}")]
    private static partial void LogLostRace(ILogger logger, Guid targetId, string constraint);
}

/// <summary>A FiringChief write found the registry locked (BR-10); <see cref="RegistryWriteGuard.RunAsync{T}"/> turns it into an outcome.</summary>
#pragma warning disable CA1064 // Internal by design: it never leaves the module.
internal sealed class RegistryLockedException : Exception
#pragma warning restore CA1064
{
    public RegistryLockedException()
        : base("The registry is locked for FiringChiefs.")
    {
    }

    public RegistryLockedException(string message)
        : base(message)
    {
    }

    public RegistryLockedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
