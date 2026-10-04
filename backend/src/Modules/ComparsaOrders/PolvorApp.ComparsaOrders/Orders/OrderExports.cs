using System.Data;
using Microsoft.EntityFrameworkCore;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.ComparsaOrders.Entries;
using PolvorApp.ComparsaOrders.Loans;
using PolvorApp.ComparsaOrders.Persistence;

namespace PolvorApp.ComparsaOrders.Orders;

/// <summary>
/// The orders as the exports read them (change add-exports, design D2); unscoped, read-only. The orders,
/// entries and loans are read in one repeatable-read transaction, so a file never mixes states.
/// </summary>
internal sealed class OrderExports(ComparsaOrdersDbContext db) : IOrderExports
{
    public async Task<IReadOnlyList<ExportedOrder>> ListValidatedAsync(Guid editionId, CancellationToken cancellationToken)
    {
        await using var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var orders = await db.Orders.AsNoTracking()
            .Where(o => o.EditionId == editionId && o.Status == OrderStatus.Validated)
            .ToListAsync(cancellationToken);
        return await WithEntriesAsync(orders, cancellationToken);
    }

    public async Task<ExportedOrder?> FindAsync(Guid editionId, Guid comparsaId, CancellationToken cancellationToken)
    {
        await using var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var order = await db.Orders.AsNoTracking()
            .SingleOrDefaultAsync(o => o.EditionId == editionId && o.ComparsaId == comparsaId, cancellationToken);
        if (order is null)
        {
            return null;
        }

        var exported = await WithEntriesAsync([order], cancellationToken);
        return exported.Single();
    }

    /// <summary>The entries and loans of the orders, read once each: under a thousand rows per edition.</summary>
    private async Task<List<ExportedOrder>> WithEntriesAsync(List<ComparsaOrder> orders, CancellationToken cancellationToken)
    {
        Guid[] orderIds = [.. orders.Select(o => o.Id)];
        var entries = orderIds.Length == 0
            ? []
            : await db.Entries.AsNoTracking().Where(e => orderIds.Contains(e.OrderId)).ToListAsync(cancellationToken);
        Guid[] entryIds = [.. entries.Where(e => e.WeaponSource == WeaponSource.Loan).Select(e => e.Id)];
        var loans = entryIds.Length == 0
            ? []
            : await db.Loans.AsNoTracking().Where(l => entryIds.Contains(l.EntryId)).ToDictionaryAsync(l => l.EntryId, cancellationToken);
        var byOrder = entries.ToLookup(e => e.OrderId);
        return [.. orders.Select(o => new ExportedOrder(
            o.Id, o.ComparsaId, o.Status, [.. byOrder[o.Id].Select(e => Exported(e, loans.GetValueOrDefault(e.Id)))]))];
    }

    private static ExportedEntry Exported(EditionEntry entry, WeaponLoan? loan) => new(
        entry.Id,
        entry.ArquebusierId,
        entry.Status == ArquebusierStatus.Active,
        entry.PowderKg,
        entry.CapsBoxes,
        entry.CapsType,
        entry.WeaponSource,
        entry.OwnedWeaponId,
        entry.RentalWeaponModelId,
        entry.Flask,
        new ExportedPerson(entry.FirstName, entry.LastName, entry.NationalId, entry.FederationId),
        entry.WeaponSource == WeaponSource.Owned
            ? new ExportedWeapon(entry.OwnedWeaponModelId, entry.OwnedWeaponNumber, entry.OwnedWeaponGuideNumber)
            : null,
        loan is null
            ? null
            : new ExportedLoan(
                loan.LenderKind,
                new ExportedPerson(loan.LenderFirstName, loan.LenderLastName, loan.LenderNationalId, null),
                loan.LenderComparsaId,
                new ExportedWeapon(loan.WeaponModelId, loan.WeaponNumber, loan.OwnershipGuideNumber)),
        entry.ErasedAt is not null);
}
