using System.Data.Common;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.FestivalEditions.Contracts;
using PolvorApp.IdentityAccess.Contracts;

namespace PolvorApp.ComparsaOrders.Orders;

/// <summary>
/// The edit rule of BR-10 (spec: Who may edit orders; design D4, D8), checked on the edition read
/// <c>FOR SHARE</c> in the write's own transaction, so it cannot change before the write commits.
/// </summary>
internal sealed class OrderGate(IEditionDirectory editions, ICurrentUser currentUser)
{
    /// <summary>
    /// The edition when the caller may edit its orders, otherwise why not: a missing edition, or a
    /// draft for a FiringChief, is not found; a draft is not started for an Admin; closed orders refuse
    /// a FiringChief. Admins edit in any non-draft edition, open or closed.
    /// </summary>
    public async Task<(OrderOutcome Outcome, EditionSnapshot? Edition)> CheckEditAsync(
        Guid editionId, DbTransaction transaction, CancellationToken cancellationToken)
    {
        var edition = await editions.ReadForOrderWriteAsync(editionId, transaction, cancellationToken);
        return edition switch
        {
            null => (OrderOutcome.NotFound, null),
            { Status: EditionStatus.Draft } when !currentUser.IsAdmin => (OrderOutcome.NotFound, null),
            { Status: EditionStatus.Draft } => (OrderOutcome.EditionNotStarted, null),
            { OrdersOpen: false } when !currentUser.IsAdmin => (OrderOutcome.Closed, null),
            _ => (OrderOutcome.Done, edition),
        };
    }

    /// <summary>A FiringChief may not edit a validated order (spec: Who may edit orders (BR-10)).</summary>
    public OrderOutcome CheckOrder(ComparsaOrder order)
    {
        ArgumentNullException.ThrowIfNull(order);
        return !currentUser.IsAdmin && order.Status == OrderStatus.Validated ? OrderOutcome.Validated : OrderOutcome.Done;
    }

    /// <summary>
    /// After a FiringChief's edit, a submitted order goes back to <c>DRAFT</c> (maintainer decision). Every
    /// edit touches the order, so its version changes and a stale submit is refused.
    /// </summary>
    public void Touch(ComparsaOrder order, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(order);
        if (!currentUser.IsAdmin && order.Status == OrderStatus.Submitted)
        {
            order.Status = OrderStatus.Draft;
        }

        order.UpdatedAt = now;
    }
}
