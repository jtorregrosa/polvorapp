using Microsoft.EntityFrameworkCore;
using PolvorApp.ComparsaOrders.Persistence;
using PolvorApp.FederationCatalog.Contracts;

namespace PolvorApp.ComparsaOrders;

/// <summary>
/// The orders' veto on catalogue deletions (spec federation-catalog: Deleting comparsas and weapon
/// models; design D5): a comparsa with an order or named by a loan, and a weapon model rented, kept in
/// an owned weapon's copy or lent, are in use. The foreign keys back this up.
/// </summary>
internal sealed class OrdersCatalogUsage(ComparsaOrdersDbContext db) : ICatalogUsage
{
    public async Task<bool> IsComparsaInUseAsync(Guid comparsaId, CancellationToken cancellationToken) =>
        await db.Orders.AnyAsync(o => o.ComparsaId == comparsaId, cancellationToken)
        || await db.Loans.AnyAsync(l => l.LenderComparsaId == comparsaId, cancellationToken);

    public async Task<bool> IsWeaponModelInUseAsync(Guid weaponModelId, CancellationToken cancellationToken) =>
        await db.Entries.AnyAsync(e => e.RentalWeaponModelId == weaponModelId || e.OwnedWeaponModelId == weaponModelId, cancellationToken)
        || await db.Loans.AnyAsync(l => l.WeaponModelId == weaponModelId, cancellationToken);
}
