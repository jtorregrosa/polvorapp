using PolvorApp.ComparsaOrders.Contracts;

namespace PolvorApp.ComparsaOrders.Orders;

/// <summary>A change of an order's status (specs: Submitting an order (UC-14), Reviewing orders (UC-15)).</summary>
internal enum OrderMove
{
    Submit,
    Validate,
    Return,
}

/// <summary>
/// Which moves each role may make (design D8). A FiringChief and an Admin submit a draft or returned
/// order; only an Admin validates (also an order never submitted, to close it) and returns. Any other
/// move is <c>orders.invalidTransition</c>; a role that may not move at all gets 403 at the endpoint.
/// </summary>
internal static class OrderStatusMoves
{
    public static bool IsAllowed(OrderMove move, OrderStatus from, bool admin) => move switch
    {
        OrderMove.Submit => from is OrderStatus.Draft or OrderStatus.Returned,
        OrderMove.Validate => admin && from is OrderStatus.Submitted or OrderStatus.Draft or OrderStatus.Returned,
        OrderMove.Return => admin && from is OrderStatus.Submitted or OrderStatus.Validated,
        _ => throw new ArgumentOutOfRangeException(nameof(move), move, "Unknown move."),
    };

    public static OrderStatus Target(OrderMove move) => move switch
    {
        OrderMove.Submit => OrderStatus.Submitted,
        OrderMove.Validate => OrderStatus.Validated,
        OrderMove.Return => OrderStatus.Returned,
        _ => throw new ArgumentOutOfRangeException(nameof(move), move, "Unknown move."),
    };
}
