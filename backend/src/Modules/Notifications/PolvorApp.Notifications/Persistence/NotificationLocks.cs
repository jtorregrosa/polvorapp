using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace PolvorApp.Notifications.Persistence;

/// <summary>
/// Transactions and advisory locks of the notifications module. Its locks use the two-key form with a
/// class of their own ("PolN"), apart from every other module's keys.
/// </summary>
internal static class NotificationLocks
{
    /// <summary>"PolN": the class key of the notifications module's advisory locks.</summary>
    private const int LockClass = 0x506F6C4E;

    /// <summary>Starts a transaction with a 5 s lock timeout, as every module's writes do.</summary>
    public static async Task<IDbContextTransaction> BeginWriteAsync(this NotificationsDbContext db, CancellationToken cancellationToken)
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
    /// Serialises one user's preference saves, so the audit entry always describes what really changed.
    /// A hash collision only makes two users wait for each other.
    /// </summary>
    public static Task LockPreferencesAsync(this NotificationsDbContext db, Guid userId, CancellationToken cancellationToken)
    {
        RequireTransaction(db);
        var key = Key(userId);
        return db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({LockClass}, {key})", cancellationToken);
    }

    /// <summary>
    /// One run of each scheduled step at a time (design D5), whatever the number of instances: false when
    /// another process is doing the same step, so the caller skips it instead of waiting. Steps use the keys
    /// 0 to 9, which a user's hash could only share by chance; then a save waits for a step, never the reverse.
    /// </summary>
    public static async Task<bool> TryLockScheduledStepAsync(this NotificationsDbContext db, int step, CancellationToken cancellationToken)
    {
        RequireTransaction(db);
        var locked = await db.Database
            .SqlQuery<bool>($"SELECT pg_try_advisory_xact_lock({LockClass}, {step}) AS \"Value\"")
            .ToListAsync(cancellationToken);
        return locked is [true];
    }

    /// <summary>A deterministic 32-bit FNV-1a hash of the id: the same on every process and host, unlike <see cref="HashCode"/>.</summary>
    public static int Key(Guid id)
    {
        Span<byte> bytes = stackalloc byte[16];
        id.TryWriteBytes(bytes);
        var hash = 2166136261u;
        foreach (var b in bytes)
        {
            hash = (hash ^ b) * 16777619u;
        }

        return unchecked((int)hash);
    }

    private static void RequireTransaction(NotificationsDbContext db)
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Notification locks need an explicit transaction.");
        }
    }
}
