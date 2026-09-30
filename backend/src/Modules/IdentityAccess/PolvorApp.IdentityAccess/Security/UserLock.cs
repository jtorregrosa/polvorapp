using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PolvorApp.IdentityAccess.Persistence;
using PolvorApp.IdentityAccess.Users;

namespace PolvorApp.IdentityAccess.Security;

/// <summary>
/// Serialises authentication attempts on one user: a transaction holding <c>FOR UPDATE</c> on the
/// user's row, with the entity reloaded after the lock. Identity counts failures with a
/// read-modify-write guarded by the concurrency stamp, so without this, parallel wrong guesses
/// would be lost and bypass the lockout (security review of group 4, H1). Everything the attempt
/// writes — counters, tokens, audit entries — commits together.
/// </summary>
internal static class UserLock
{
    /// <summary>Advisory lock key serialising every change that can add or remove an Admin.</summary>
    private const long AdminChangesKey = 0x506F6C766F72_41; // "Polvor" + 'A'

    /// <summary>
    /// Serialises changes that can remove an active Admin (spec: last Admin is protected): a
    /// transaction-scoped advisory lock, then the target's row (or none, for create-admin).
    /// </summary>
    public static async Task<IDbContextTransaction> LockAdminsAsync(this IdentityAccessDbContext db, User? user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '5s'", cancellationToken);
            await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({AdminChangesKey})", cancellationToken);
            if (user is not null)
            {
                await db.Database.ExecuteSqlAsync($"SELECT 1 FROM identity.users WHERE id = {user.Id} FOR UPDATE", cancellationToken);
                await db.Entry(user).ReloadAsync(cancellationToken);
            }

            return transaction;
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }

    public static async Task<IDbContextTransaction> LockAsync(this IdentityAccessDbContext db, User user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(user);
        var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // A longer wait means a flood on one account: fail the request instead of holding connections.
            await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '5s'", cancellationToken);
            await db.Database.ExecuteSqlAsync(
                $"SELECT 1 FROM identity.users WHERE id = {user.Id} FOR UPDATE", cancellationToken);
            await db.Entry(user).ReloadAsync(cancellationToken);
            return transaction;
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }
}
