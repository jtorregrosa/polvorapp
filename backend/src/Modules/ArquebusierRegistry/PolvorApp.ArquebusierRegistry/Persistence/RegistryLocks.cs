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
    /// <summary>Advisory lock key serialising imports, after the identity module's Admin key ("Polvor" + 'A').</summary>
    public const long ImportLockKey = 0x506F6C766F72_49; // "Polvor" + 'I'

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
    /// For an import (add-registry-import): imports run one at a time, so the second of two imports
    /// of the same people sees the first one's rows and reports them as taken. Without it, both would
    /// insert hundreds of rows in key order, which differs between requests, and could deadlock. The
    /// lock ends with the transaction; waiting for it is bounded by the 5 s lock timeout (503).
    /// </summary>
    public static async Task LockImportsAsync(this ArquebusierRegistryDbContext db, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("The import lock needs an explicit transaction.");
        }

        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({ImportLockKey})", cancellationToken);
    }

    /// <summary>
    /// For a transfer or deletion: <c>FOR UPDATE</c> waits for and then blocks every other change to
    /// the arquebusier and its owned weapons. False when the row does not exist or is outside the scope.
    /// </summary>
    public static Task<bool> LockArquebusierForUpdateAsync(this ArquebusierRegistryDbContext db, Guid id, ComparsaAccess access, CancellationToken cancellationToken) =>
        LockAsync(db, id, access, RowLock.Update, cancellationToken);

    /// <summary>
    /// For an edit: <c>FOR NO KEY UPDATE</c> serialises edits and conflicts with a transfer or
    /// deletion, but not with owned-weapon writes (which share the key). An edit of a unique column
    /// (nationalId, federationId) is a key update for PostgreSQL: its UPDATE then waits for in-flight
    /// weapon writes, at worst until the lock timeout (503). There is no cycle, so no deadlock. Edits
    /// also wait for in-flight photo writes (<c>FOR SHARE</c>), bounded by the same timeout.
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

    /// <summary>
    /// For a photo write (add-arquebusier-photos, design D6): <c>FOR SHARE</c> also conflicts with
    /// the UPDATE of an edit, so a license photo never slips in while the same license is being
    /// removed, and a photo never lands on an arquebusier that just left the caller's scope. Photo
    /// writes do not block each other.
    /// </summary>
    public static Task<bool> LockArquebusierForShareAsync(this ArquebusierRegistryDbContext db, Guid id, ComparsaAccess access, CancellationToken cancellationToken) =>
        LockAsync(db, id, access, RowLock.Share, cancellationToken);

    /// <summary>
    /// Whether <paramref name="exception"/> is PostgreSQL giving up on a lock after the timeout, or
    /// aborting one side of a deadlock (two edits swapping unique values): both are worth a retry.
    /// The database error can sit several levels deep: a save wraps it in a
    /// <see cref="Microsoft.EntityFrameworkCore.DbUpdateException"/>, which the execution strategy
    /// wraps again when it deems the error transient.
    /// </summary>
    public static bool IsRetryable(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres)
            {
                return postgres.SqlState is PostgresErrorCodes.LockNotAvailable or PostgresErrorCodes.DeadlockDetected;
            }
        }

        return false;
    }

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
            RowLock.Share => db.Database.SqlQuery<int>(
                $"SELECT 1 AS \"Value\" FROM registry.arquebusiers WHERE id = {id} AND ({all} OR comparsa_id = ANY({comparsaIds})) FOR SHARE"),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown lock mode."),
        };
        return await rows.ToListAsync(cancellationToken) is [_];
    }

    private enum RowLock
    {
        Update,
        NoKeyUpdate,
        KeyShare,
        Share,
    }
}
