using Microsoft.EntityFrameworkCore;
using PolvorApp.ComparsaOrders.Persistence;
using PolvorApp.FestivalEditions.Contracts;

namespace PolvorApp.ComparsaOrders;

/// <summary>
/// The orders' veto on edition deletions (spec festival-editions: Edition management by Admins): a
/// draft edition with comparsa orders is in use. The foreign key backs this up (design D5).
/// </summary>
internal sealed class OrdersEditionUsage(ComparsaOrdersDbContext db) : IEditionUsage
{
    public Task<bool> IsEditionInUseAsync(Guid editionId, CancellationToken cancellationToken) =>
        db.Orders.AnyAsync(o => o.EditionId == editionId, cancellationToken);
}
