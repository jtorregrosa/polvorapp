using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace PolvorApp.SharedKernel.Persistence;

/// <summary>
/// Runs a module context inside a transaction another module started, e.g. a GDPR erasure that spans
/// every module (add-audit-privacy, design D6). The context must not have opened a connection yet.
/// </summary>
public static class SharedTransactions
{
    /// <summary>Makes <paramref name="db"/> use <paramref name="transaction"/> and its connection, which it never closes.</summary>
    public static async Task EnlistAsync(this DbContext db, DbTransaction transaction, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(transaction);
        if (db.Database.CurrentTransaction?.GetDbTransaction() == transaction)
        {
            return;
        }

        db.Database.SetDbConnection(
            transaction.Connection ?? throw new InvalidOperationException("The transaction has no connection."), contextOwnsConnection: false);
        await db.Database.UseTransactionAsync(transaction, cancellationToken);
    }
}
