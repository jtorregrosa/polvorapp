using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace PolvorApp.ComparsaOrders.Persistence;

/// <summary>
/// The one save of an order write, with every lost race mapped to its blocking outcome (design D3, D8):
/// a version that changed meanwhile, a unique index another request won, or a registry or catalogue row
/// deleted meanwhile (<c>23503</c>, answered <c>orders.modified</c> so the UI reloads).
/// </summary>
internal static class OrderSaves
{
    private static readonly HashSet<string> RaceForeignKeys =
    [
        ComparsaOrdersDbContext.ArquebusierForeignKey,
        ComparsaOrdersDbContext.OwnedWeaponForeignKey,
        ComparsaOrdersDbContext.LoanOwnedWeaponForeignKey,
        ComparsaOrdersDbContext.EditionForeignKey,
        ComparsaOrdersDbContext.ComparsaForeignKey,
        ComparsaOrdersDbContext.LoanComparsaForeignKey,
        ComparsaOrdersDbContext.RentalModelForeignKey,
        ComparsaOrdersDbContext.OwnedWeaponModelForeignKey,
        ComparsaOrdersDbContext.LoanWeaponModelForeignKey,
    ];

    /// <param name="onEntryIndex">The outcome when another request gave one of these arquebusiers an entry in the edition first.</param>
    /// <param name="onConcurrency">
    /// The outcome when a row changed between its load and this save: an entry write answers
    /// <c>entries.modified</c>, an order move <c>orders.modified</c>.
    /// </param>
    public static async Task<OrderOutcome> SaveAsync(
        this ComparsaOrdersDbContext db,
        OrderWriteGuard guard,
        Guid? orderId,
        OrderOutcome onEntryIndex,
        CancellationToken cancellationToken,
        OrderOutcome onConcurrency = OrderOutcome.EntryModified)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return OrderOutcome.Done;
        }
        catch (DbUpdateConcurrencyException exception)
        {
            // Only a registry SET NULL can bump an entry or a loan between its load and this save: the order is locked.
            db.ChangeTracker.Clear();
            guard.LostRace(orderId, "concurrency:" + string.Join(',', exception.Entries.Select(e => e.Metadata.ClrType.Name).Distinct()));
            return onConcurrency;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { ConstraintName: { } constraint } postgres
            && Race(postgres.SqlState, constraint) is { } outcome)
        {
            db.ChangeTracker.Clear();
            guard.LostRace(orderId, constraint);
            return outcome == OrderOutcome.AlreadyInEdition ? onEntryIndex : outcome;
        }
    }

    private static OrderOutcome? Race(string sqlState, string constraint) => (sqlState, constraint) switch
    {
        (PostgresErrorCodes.UniqueViolation, ComparsaOrdersDbContext.OrderIndex) => OrderOutcome.AlreadyPrepared,
        (PostgresErrorCodes.UniqueViolation, ComparsaOrdersDbContext.EntryIndex) => OrderOutcome.AlreadyInEdition,
        (PostgresErrorCodes.ForeignKeyViolation, _) when RaceForeignKeys.Contains(constraint) => OrderOutcome.Modified,
        _ => null,
    };
}
