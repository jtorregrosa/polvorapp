using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using PolvorApp.IdentityAccess.Contracts;

namespace PolvorApp.ArquebusierRegistry.Persistence;

/// <summary>
/// Transactions and row locks of the registry (design D10, locking rules). Locks are taken with
/// <c>SELECT 1 … FOR …</c> and the row is loaded afterwards with LINQ, because <c>SELECT *</c> omits
/// the <c>xmin</c> system column the entities carry as their version; a lock does not change
/// <c>xmin</c>. The lock query carries the caller's comparsa scope, so a row outside it is never
/// locked and behaves exactly like an unknown id (BR-12). Every transaction sets a 5 s lock timeout,
/// as the catalog does, so a stuck catalog deletion cannot hold a registry request forever.
/// </summary>
internal static class RegistryLocks
{
    /// <summary>Starts the transaction every registry write runs in.</summary>
    public static async Task<IDbContextTransaction> BeginWriteAsync(this ArquebusierRegistryDbContext db, CancellationToken cancellationToken)
    {
        var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '5s'", cancellationToken);
            return transaction;
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// For a transfer or deletion: <c>FOR UPDATE</c> waits for and then blocks every other change to
    /// the arquebusier and its owned weapons. False when the row does not exist or is outside the scope.
    /// </summary>
    public static Task<bool> LockArquebusierForUpdateAsync(this ArquebusierRegistryDbContext db, Guid id, ComparsaAccess access, CancellationToken cancellationToken) =>
        LockAsync(db, id, access, RowLock.Update, cancellationToken);

    /// <summary>
    /// For an edit that keeps the key: <c>FOR NO KEY UPDATE</c> serialises edits and conflicts with a
    /// transfer or deletion, but not with owned-weapon writes (which share the key).
    /// </summary>
    public static Task<bool> LockArquebusierForChangeAsync(this ArquebusierRegistryDbContext db, Guid id, ComparsaAccess access, CancellationToken cancellationToken) =>
        LockAsync(db, id, access, RowLock.NoKeyUpdate, cancellationToken);

    /// <summary>
    /// For an owned-weapon write: <c>FOR KEY SHARE</c> conflicts only with the <c>FOR UPDATE</c> of a
    /// transfer or deletion, so a weapon change never lands on an arquebusier that just left the
    /// caller's scope.
    /// </summary>
    public static Task<bool> LockArquebusierForKeyShareAsync(this ArquebusierRegistryDbContext db, Guid id, ComparsaAccess access, CancellationToken cancellationToken) =>
        LockAsync(db, id, access, RowLock.KeyShare, cancellationToken);

    /// <summary>Whether <paramref name="exception"/> is PostgreSQL giving up on a lock after the timeout.</summary>
    public static bool IsLockTimeout(Exception exception) =>
        exception is PostgresException { SqlState: PostgresErrorCodes.LockNotAvailable }
        || exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.LockNotAvailable };

    private static async Task<bool> LockAsync(
        ArquebusierRegistryDbContext db, Guid id, ComparsaAccess access, RowLock mode, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Registry row locks need an explicit transaction.");
        }

        // Each mode is literal SQL (lock clauses cannot be parameters); id and scope are parameters.
        var all = access.IsAll;
        var comparsaIds = access.ComparsaIds.ToArray();
        var rows = mode switch
        {
            RowLock.Update => db.Database.SqlQuery<int>(
                $"SELECT 1 AS \"Value\" FROM registry.arquebusiers WHERE id = {id} AND ({all} OR comparsa_id = ANY({comparsaIds})) FOR UPDATE"),
            RowLock.NoKeyUpdate => db.Database.SqlQuery<int>(
                $"SELECT 1 AS \"Value\" FROM registry.arquebusiers WHERE id = {id} AND ({all} OR comparsa_id = ANY({comparsaIds})) FOR NO KEY UPDATE"),
            RowLock.KeyShare => db.Database.SqlQuery<int>(
                $"SELECT 1 AS \"Value\" FROM registry.arquebusiers WHERE id = {id} AND ({all} OR comparsa_id = ANY({comparsaIds})) FOR KEY SHARE"),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown lock mode."),
        };
        return await rows.ToListAsync(cancellationToken) is [_];
    }

    private enum RowLock
    {
        Update,
        NoKeyUpdate,
        KeyShare,
    }
}
