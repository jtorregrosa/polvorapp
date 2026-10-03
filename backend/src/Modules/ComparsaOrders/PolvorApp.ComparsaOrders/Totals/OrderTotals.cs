using System.Collections.Frozen;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ComparsaOrders.Entries;

namespace PolvorApp.ComparsaOrders.Totals;

/// <summary>
/// The totals of one or several orders (spec: Order totals and dashboard (UC-16); design D10), derived
/// from their entries and never stored. Anonymised history entries and entries of arquebusiers who
/// left the registry count like any other.
/// </summary>
/// <param name="Active">Entries with status <c>ACTIVE</c>.</param>
/// <param name="Reserve">Entries with status <c>RESERVE</c>.</param>
/// <param name="PowderKg">Powder in kilograms.</param>
/// <param name="NormalCapsBoxes">Caps boxes of type <c>NORMAL</c>.</param>
/// <param name="SmallCapsBoxes">Caps boxes of type <c>SMALL</c>.</param>
/// <param name="WeaponRentals">Weapon rentals by model.</param>
/// <param name="FlaskRentals1Kg">Rented 1 kg flasks.</param>
/// <param name="FlaskRentals2Kg">Rented 2 kg flasks.</param>
/// <param name="Loans">Entries with a lent weapon.</param>
/// <param name="OwnedWeapons">Entries with an owned weapon.</param>
/// <param name="EntriesWithWarnings"><c>ACTIVE</c> entries with at least one compliance warning, for the edition in progress.</param>
internal sealed record OrderTotals(
    int Active,
    int Reserve,
    int PowderKg,
    int NormalCapsBoxes,
    int SmallCapsBoxes,
    IReadOnlyDictionary<Guid, int> WeaponRentals,
    int FlaskRentals1Kg,
    int FlaskRentals2Kg,
    int Loans,
    int OwnedWeapons,
    int EntriesWithWarnings)
{
    public static readonly OrderTotals Zero = new(0, 0, 0, 0, 0, FrozenDictionary<Guid, int>.Empty, 0, 0, 0, 0, 0);

    /// <param name="entries">The entries.</param>
    /// <param name="entriesWithWarnings">Which of them have compliance warnings now.</param>
    public static OrderTotals Of(IReadOnlyCollection<EditionEntry> entries, IReadOnlySet<Guid> entriesWithWarnings)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(entriesWithWarnings);
        return new OrderTotals(
            entries.Count(e => e.Status == ArquebusierStatus.Active),
            entries.Count(e => e.Status == ArquebusierStatus.Reserve),
            entries.Sum(e => e.PowderKg),
            entries.Where(e => e.CapsType == CapsType.Normal).Sum(e => e.CapsBoxes),
            entries.Where(e => e.CapsType == CapsType.Small).Sum(e => e.CapsBoxes),
            entries
                .Where(e => e.WeaponSource == WeaponSource.Rental && e.RentalWeaponModelId is not null)
                .GroupBy(e => e.RentalWeaponModelId!.Value)
                .ToDictionary(g => g.Key, g => g.Count()),
            entries.Count(e => e.Flask == FlaskOption.Rental1Kg),
            entries.Count(e => e.Flask == FlaskOption.Rental2Kg),
            entries.Count(e => e.WeaponSource == WeaponSource.Loan),
            entries.Count(e => e.WeaponSource == WeaponSource.Owned),
            entries.Count(e => entriesWithWarnings.Contains(e.Id)));
    }

    /// <summary>The totals of several orders together.</summary>
    public static OrderTotals Sum(IEnumerable<OrderTotals> totals)
    {
        ArgumentNullException.ThrowIfNull(totals);
        return totals.Aggregate(Zero, (sum, next) => new OrderTotals(
            sum.Active + next.Active,
            sum.Reserve + next.Reserve,
            sum.PowderKg + next.PowderKg,
            sum.NormalCapsBoxes + next.NormalCapsBoxes,
            sum.SmallCapsBoxes + next.SmallCapsBoxes,
            sum.WeaponRentals.Concat(next.WeaponRentals).GroupBy(p => p.Key).ToDictionary(g => g.Key, g => g.Sum(p => p.Value)),
            sum.FlaskRentals1Kg + next.FlaskRentals1Kg,
            sum.FlaskRentals2Kg + next.FlaskRentals2Kg,
            sum.Loans + next.Loans,
            sum.OwnedWeapons + next.OwnedWeapons,
            sum.EntriesWithWarnings + next.EntriesWithWarnings));
    }
}
