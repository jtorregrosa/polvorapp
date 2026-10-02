using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace PolvorApp.FestivalEditions.Persistence;

/// <summary>
/// Transactions and row locks of the editions (design D3). Every write locks the edition row
/// <c>FOR UPDATE</c> first, so edits, moves, orders changes, model sets and milestones of one
/// edition are serialised; at this scale that costs nothing and keeps the milestone cap exact. The
/// lock is taken with <c>SELECT 1</c> and the row loaded afterwards with LINQ, because
/// <c>SELECT *</c> omits the <c>xmin</c> system column the edition carries as its version.
/// </summary>
internal static class EditionLocks
{
    /// <summary>Starts the transaction every edition write runs in, with a 5 s lock timeout.</summary>
    public static async Task<IDbContextTransaction> BeginWriteAsync(this FestivalEditionsDbContext db, CancellationToken cancellationToken)
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

    /// <summary>Locks the edition until the transaction ends; false when it does not exist (the id is the primary key).</summary>
    public static async Task<bool> LockEditionAsync(this FestivalEditionsDbContext db, Guid id, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Edition row locks need an explicit transaction.");
        }

        var rows = await db.Database
            .SqlQuery<int>($"SELECT 1 AS \"Value\" FROM editions.festival_editions WHERE id = {id} FOR UPDATE")
            .ToListAsync(cancellationToken);
        return rows is [_];
    }
}
