using PolvorApp.FestivalEditions;
using PolvorApp.FestivalEditions.Contracts;
using PolvorApp.FestivalEditions.Editions;

namespace PolvorApp.Api.Tests.Editions;

/// <summary>Spec "Edition lifecycle (UC-11)", design D3: adjacent moves, completeness and closed orders.</summary>
public sealed class EditionStatusMovesTests
{
    public static TheoryData<EditionStatus, EditionStatus, bool> Pairs()
    {
        var data = new TheoryData<EditionStatus, EditionStatus, bool>();
        var order = new[] { EditionStatus.Draft, EditionStatus.InProgress, EditionStatus.Closed };
        for (var from = 0; from < order.Length; from++)
        {
            for (var to = 0; to < order.Length; to++)
            {
                data.Add(order[from], order[to], Math.Abs(from - to) == 1);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void Only_one_step_moves_are_allowed(EditionStatus from, EditionStatus to, bool allowed)
    {
        var edition = Complete(from);

        var (outcome, _) = EditionStatusMoves.Check(edition, to);

        Assert.Equal(allowed ? EditionOutcome.Done : EditionOutcome.InvalidTransition, outcome);
    }

    [Fact]
    public void Starting_needs_the_festival_dates_both_window_dates_and_every_price()
    {
        var edition = Complete(EditionStatus.Draft);
        edition.OrdersCloseOn = null;
        edition.FlaskRental = null;
        edition.PowderPerKg = null;

        var (outcome, missing) = EditionStatusMoves.Check(edition, EditionStatus.InProgress);

        Assert.Equal(EditionOutcome.Incomplete, outcome);
        Assert.Equal(["ordersCloseOn", "prices.powderPerKg", "prices.flaskRental"], missing);
    }

    [Fact]
    public void An_empty_draft_lists_every_missing_field_in_form_order()
    {
        var edition = Complete(EditionStatus.Draft);
        (edition.OrdersOpenOn, edition.OrdersCloseOn) = (null, null);
        (edition.PowderPerKg, edition.CapsBox, edition.WeaponRental, edition.FlaskRental) = (null, null, null, null);

        var (_, missing) = EditionStatusMoves.Check(edition, EditionStatus.InProgress);

        Assert.Equal(["ordersOpenOn", "ordersCloseOn", "prices.powderPerKg", "prices.capsBox", "prices.weaponRental", "prices.flaskRental"], missing);
    }

    [Fact]
    public void A_price_of_zero_counts_as_set()
    {
        var edition = Complete(EditionStatus.Draft);
        edition.CapsBox = 0m;

        Assert.Equal(EditionOutcome.Done, EditionStatusMoves.Check(edition, EditionStatus.InProgress).Outcome);
    }

    [Fact]
    public void Reopening_a_closed_edition_does_not_check_completeness()
    {
        var edition = Complete(EditionStatus.Closed);
        edition.FlaskRental = null;

        Assert.Equal(EditionOutcome.Done, EditionStatusMoves.Check(edition, EditionStatus.InProgress).Outcome);
    }

    [Theory]
    [InlineData(EditionStatus.Closed)]
    [InlineData(EditionStatus.Draft)]
    public void Leaving_the_edition_in_progress_needs_its_orders_closed(EditionStatus to)
    {
        var edition = Complete(EditionStatus.InProgress);
        edition.OrdersOpen = true;

        Assert.Equal(EditionOutcome.OrdersOpen, EditionStatusMoves.Check(edition, to).Outcome);
    }

    [Fact]
    public void Staying_in_the_same_status_is_not_a_move()
    {
        Assert.Equal(EditionOutcome.InvalidTransition, EditionStatusMoves.Check(Complete(EditionStatus.InProgress), EditionStatus.InProgress).Outcome);
    }

    private static FestivalEdition Complete(EditionStatus status) => new()
    {
        Id = Guid.CreateVersion7(),
        Year = 2031,
        FestivalStartsOn = new DateOnly(2031, 4, 22),
        FestivalEndsOn = new DateOnly(2031, 4, 25),
        OrdersOpenOn = new DateOnly(2031, 1, 10),
        OrdersCloseOn = new DateOnly(2031, 2, 10),
        Status = status,
        PowderPerKg = 55m,
        CapsBox = 4.5m,
        WeaponRental = 30m,
        FlaskRental = 6m,
        CreatedAt = DateTimeOffset.UtcNow,
    };
}
