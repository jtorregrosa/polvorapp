using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.ComparsaOrders.Entries;
using PolvorApp.ComparsaOrders.Totals;
using static PolvorApp.Api.Tests.Infrastructure.OrderData;

namespace PolvorApp.Api.Tests.Orders;

/// <summary>Spec "Order totals and dashboard (UC-16)": every total, from the entries (design D10).</summary>
public sealed class OrderTotalsTests
{
    private static readonly Guid Arcabuz = Guid.CreateVersion7();
    private static readonly Guid Trabuco = Guid.CreateVersion7();

    [Fact]
    public void Every_total_is_counted_from_the_entries()
    {
        var entries = new[]
        {
            Entry(e => (e.PowderKg, e.CapsBoxes, e.CapsType, e.WeaponSource, e.RentalWeaponModelId, e.Flask) = (2, 3, CapsType.Normal, WeaponSource.Rental, Arcabuz, FlaskOption.Rental2Kg)),
            Entry(e => (e.PowderKg, e.WeaponSource, e.RentalWeaponModelId, e.Flask) = (1, WeaponSource.Rental, Arcabuz, FlaskOption.Rental1Kg)),
            Entry(e => (e.CapsBoxes, e.CapsType, e.WeaponSource, e.RentalWeaponModelId) = (2, CapsType.Small, WeaponSource.Rental, Trabuco)),
            Entry(e => (e.PowderKg, e.WeaponSource) = (2, WeaponSource.Owned)),
            Entry(e => e.WeaponSource = WeaponSource.Loan),
            Entry(e => e.Status = ArquebusierStatus.Reserve),
        };
        var withWarnings = new HashSet<Guid> { entries[0].Id, entries[3].Id };

        var totals = OrderTotals.Of(entries, withWarnings);

        Assert.Equal((5, 1, 5, 3, 2, 1, 1, 1, 1, 2), (
            totals.Active, totals.Reserve, totals.PowderKg, totals.NormalCapsBoxes, totals.SmallCapsBoxes,
            totals.FlaskRentals1Kg, totals.FlaskRentals2Kg, totals.Loans, totals.OwnedWeapons, totals.EntriesWithWarnings));
        Assert.Equal(new Dictionary<Guid, int> { [Arcabuz] = 2, [Trabuco] = 1 }, totals.WeaponRentals);
    }

    [Fact]
    public void An_empty_order_has_zero_totals() =>
        Assert.Equivalent(OrderTotals.Zero, OrderTotals.Of([], new HashSet<Guid>()));

    [Fact]
    public void Totals_of_several_orders_add_up()
    {
        var first = OrderTotals.Of([Entry(e => (e.PowderKg, e.WeaponSource, e.RentalWeaponModelId) = (2, WeaponSource.Rental, Arcabuz))], new HashSet<Guid>());
        var second = OrderTotals.Of([Entry(e => (e.PowderKg, e.WeaponSource, e.RentalWeaponModelId) = (1, WeaponSource.Rental, Arcabuz)), Entry(e => e.Status = ArquebusierStatus.Reserve)], new HashSet<Guid>());

        var sum = OrderTotals.Sum([first, second]);

        Assert.Equal((2, 1, 3), (sum.Active, sum.Reserve, sum.PowderKg));
        Assert.Equal(new Dictionary<Guid, int> { [Arcabuz] = 2 }, sum.WeaponRentals);
    }

    private static EditionEntry Entry(Action<EditionEntry> change)
    {
        var entry = NewEntry(NewOrder(NewEdition(2031), Guid.CreateVersion7()), null);
        change(entry);
        return entry;
    }
}
