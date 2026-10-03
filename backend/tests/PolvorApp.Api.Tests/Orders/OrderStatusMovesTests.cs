using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.ComparsaOrders.Orders;

namespace PolvorApp.Api.Tests.Orders;

/// <summary>Specs "Submitting an order (UC-14)" and "Reviewing orders (UC-15)": the moves each role may make (design D8).</summary>
public sealed class OrderStatusMovesTests
{
    /// <summary>Every move, from every status, for both roles; the move by name because the enum is internal.</summary>
    public static TheoryData<string, OrderStatus, bool, bool> Moves()
    {
        var data = new TheoryData<string, OrderStatus, bool, bool>();
        foreach (var move in Enum.GetValues<OrderMove>())
        {
            foreach (var from in Enum.GetValues<OrderStatus>())
            {
                foreach (var admin in new[] { false, true })
                {
                    data.Add(move.ToString(), from, admin, Allowed(move, from, admin));
                }
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Moves))]
    public void Only_the_specified_moves_pass(string move, OrderStatus from, bool admin, bool allowed) =>
        Assert.Equal(allowed, OrderStatusMoves.IsAllowed(Enum.Parse<OrderMove>(move), from, admin));

    [Theory]
    [InlineData(nameof(OrderMove.Submit), OrderStatus.Submitted)]
    [InlineData(nameof(OrderMove.Validate), OrderStatus.Validated)]
    [InlineData(nameof(OrderMove.Return), OrderStatus.Returned)]
    public void Each_move_has_its_target(string move, OrderStatus target) =>
        Assert.Equal(target, OrderStatusMoves.Target(Enum.Parse<OrderMove>(move)));

    /// <summary>The table of the spec, written out independently of the implementation.</summary>
    private static bool Allowed(OrderMove move, OrderStatus from, bool admin) => (move, from, admin) switch
    {
        (OrderMove.Submit, OrderStatus.Draft or OrderStatus.Returned, _) => true,
        (OrderMove.Validate, OrderStatus.Submitted or OrderStatus.Draft or OrderStatus.Returned, true) => true,
        (OrderMove.Return, OrderStatus.Submitted or OrderStatus.Validated, true) => true,
        _ => false,
    };
}
