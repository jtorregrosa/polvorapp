using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PolvorApp.FestivalEditions.Contracts;

namespace PolvorApp.Distribution.Persistence;

/// <summary>
/// Transactions and locks of the distribution writes (design D4). Lock order, for every path that takes
/// more than one: the edition row (<c>FOR SHARE</c>, through <see cref="IEditionDirectory"/>), then the
/// day row, then the comparsa's proxy advisory lock. Rows are locked with <c>SELECT 1</c> and loaded
/// afterwards with LINQ, because <c>SELECT *</c> omits the <c>xmin</c> column a day carries as its version.
/// </summary>
internal static class DistributionLocks
{
    /// <summary>"PolX": the class key of the proxies' two-key advisory locks (a key space apart from the one-key locks).</summary>
    private const int ProxyLockClass = 0x506F6C58;

    /// <summary>Starts the transaction every distribution write runs in, with a 5 s lock timeout.</summary>
    public static async Task<IDbContextTransaction> BeginWriteAsync(this DistributionDbContext db, CancellationToken cancellationToken)
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
    /// The edition read <c>FOR SHARE</c> on this transaction: a status move or deletion of the edition
    /// waits until the write commits, so no write commits on an edition that just left <c>IN_PROGRESS</c>.
    /// </summary>
    public static Task<EditionSnapshot?> ReadEditionForWriteAsync(this DistributionDbContext db, IEditionDirectory editions, Guid editionId, CancellationToken cancellationToken)
    {
        RequireTransaction(db);
        return editions.ReadForOrderWriteAsync(editionId, db.Database.CurrentTransaction!.GetDbTransaction(), cancellationToken);
    }

    /// <summary>Locks the day until the transaction ends; false when it does not exist.</summary>
    public static async Task<bool> LockDayAsync(this DistributionDbContext db, Guid dayId, CancellationToken cancellationToken)
    {
        RequireTransaction(db);
        var rows = await db.Database
            .SqlQuery<int>($"SELECT 1 AS \"Value\" FROM distribution.distributions WHERE id = {dayId} FOR UPDATE")
            .ToListAsync(cancellationToken);
        return rows is [_];
    }

    /// <summary>
    /// Serialises the proxy writes of one comparsa in one edition (design D4), so the absence rules hold
    /// under concurrent requests; other comparsas do not wait. A hash collision only makes two comparsas
    /// wait for each other, never breaks a rule.
    /// </summary>
    public static async Task LockProxiesAsync(this DistributionDbContext db, Guid editionId, Guid comparsaId, CancellationToken cancellationToken)
    {
        RequireTransaction(db);
        var key = PairKey(editionId, comparsaId);
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({ProxyLockClass}, {key})", cancellationToken);
    }

    /// <summary>A deterministic 32-bit FNV-1a hash of both ids: the same on every process and host, unlike <see cref="HashCode"/>.</summary>
    public static int PairKey(Guid editionId, Guid comparsaId)
    {
        Span<byte> bytes = stackalloc byte[32];
        editionId.TryWriteBytes(bytes[..16]);
        comparsaId.TryWriteBytes(bytes[16..]);
        var hash = 2166136261u;
        foreach (var b in bytes)
        {
            hash = (hash ^ b) * 16777619u;
        }

        return unchecked((int)hash);
    }

    private static void RequireTransaction(DistributionDbContext db)
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Distribution locks need an explicit transaction.");
        }
    }
}
