using PolvorApp.FestivalEditions.Contracts;

namespace PolvorApp.FestivalEditions.Editions;

/// <summary>
/// The blocking rules of a status move (spec: Edition lifecycle (UC-11); design D3). The single
/// edition in progress is checked by the database index, not here.
/// </summary>
internal static class EditionStatusMoves
{
    private static readonly EditionStatus[] Order = [EditionStatus.Draft, EditionStatus.InProgress, EditionStatus.Closed];

    /// <summary>
    /// Whether <paramref name="edition"/> may move to <paramref name="to"/>: one step only; starting
    /// needs the dates and prices (the missing field keys are listed, in form order); leaving the
    /// edition in progress needs its orders closed.
    /// </summary>
    public static (EditionOutcome Outcome, IReadOnlyList<string> Missing) Check(FestivalEdition edition, EditionStatus to)
    {
        ArgumentNullException.ThrowIfNull(edition);
        if (Math.Abs(Array.IndexOf(Order, to) - Array.IndexOf(Order, edition.Status)) != 1)
        {
            return (EditionOutcome.InvalidTransition, []);
        }

        if (edition.Status == EditionStatus.InProgress && edition.OrdersOpen)
        {
            return (EditionOutcome.OrdersOpen, []);
        }

        if (edition.Status == EditionStatus.Draft)
        {
            var missing = Missing(edition);
            if (missing.Count > 0)
            {
                return (EditionOutcome.Incomplete, missing);
            }
        }

        return (EditionOutcome.Done, []);
    }

    /// <summary>What an edition lacks to start; the festival dates are always set.</summary>
    private static List<string> Missing(FestivalEdition edition)
    {
        var missing = new List<string>();
        Add(missing, edition.OrdersOpenOn is null, "ordersOpenOn");
        Add(missing, edition.OrdersCloseOn is null, "ordersCloseOn");
        Add(missing, edition.PowderPerKg is null, "prices.powderPerKg");
        Add(missing, edition.CapsBox is null, "prices.capsBox");
        Add(missing, edition.WeaponRental is null, "prices.weaponRental");
        Add(missing, edition.FlaskRental is null, "prices.flaskRental");
        return missing;
    }

    private static void Add(List<string> missing, bool absent, string field)
    {
        if (absent)
        {
            missing.Add(field);
        }
    }
}
