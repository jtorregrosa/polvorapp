using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PolvorApp.IdentityAccess.Contracts;

namespace PolvorApp.ComparsaOrders.Persistence;

/// <summary>
/// Transactions and row locks of the orders (design D8). Every write locks the order row
/// <c>FOR UPDATE</c>, so the edits, submissions and reviews of one order are serialised. The lock
/// is taken with <c>SELECT 1</c> and the row loaded afterwards with LINQ, because <c>SELECT *</c>
/// omits the <c>xmin</c> system column the order carries as its version. Lock order, for every
/// path that takes more than one (design D3): the edition row (<c>FOR SHARE</c>), then orders by
/// id, then their entries and loans. Order writes read the registry without locking it, on another
/// connection. The registry's deletion locks the arquebusier, then the orders, before it unlinks the
/// entries; a write caught in between ends as a detected deadlock, which is retryable (README).
/// </summary>
internal static class OrderLocks
{
    /// <summary>Starts the transaction every order write runs in, with a 5 s lock timeout.</summary>
    public static async Task<IDbContextTransaction> BeginWriteAsync(this ComparsaOrdersDbContext db, CancellationToken cancellationToken)
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
    /// Locks the order until the transaction ends. The query carries the caller's comparsa scope, so
    /// an order outside it is never locked and behaves exactly like an unknown id (BR-12). False when
    /// the order does not exist or is outside the scope.
    /// </summary>
    public static async Task<bool> LockOrderAsync(this ComparsaOrdersDbContext db, Guid id, ComparsaAccess access, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Order row locks need an explicit transaction.");
        }

        var all = access.IsAll;
        var comparsaIds = access.ComparsaIds.ToArray();
        var rows = await db.Database
            .SqlQuery<int>($"SELECT 1 AS \"Value\" FROM orders.comparsa_orders WHERE id = {id} AND ({all} OR comparsa_id = ANY({comparsaIds})) FOR UPDATE")
            .ToListAsync(cancellationToken);
        return rows is [_];
    }
}
