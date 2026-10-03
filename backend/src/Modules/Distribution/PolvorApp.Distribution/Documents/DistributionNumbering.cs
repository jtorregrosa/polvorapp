using PolvorApp.SharedKernel.Text;

namespace PolvorApp.Distribution.Documents;

/// <summary>A holder of a distribution list, as the numbering orders them: their comparsa and its slot, and their name.</summary>
/// <param name="EntryId">The holder's entry, the last tie-break.</param>
/// <param name="ComparsaId">Their comparsa.</param>
/// <param name="ComparsaName">Its name.</param>
/// <param name="Slot">Its slot on the day, or null without one.</param>
/// <param name="LastName">Last name.</param>
/// <param name="FirstName">First name.</param>
internal sealed record ListHolder(Guid EntryId, Guid ComparsaId, string ComparsaName, TimeOnly? Slot, string LastName, string FirstName)
{
    /// <summary>The type name only: the names are personal data.</summary>
    public override string ToString() => nameof(ListHolder);
}

/// <summary>A holder and their number on the list.</summary>
internal sealed record NumberedHolder(int Number, ListHolder Holder);

/// <summary>
/// The global numbering of a distribution list (spec: Global numbering (UC-20); design D6): from 1 across
/// the whole day; comparsas by slot time, then by name in Spanish order for the same time, those without a
/// slot last by name; people by last name and first name in Spanish order. Derived on every generation,
/// never stored (maintainer decision): a list generated after the data changed may number differently.
/// </summary>
internal static class DistributionNumbering
{
    public static IReadOnlyList<NumberedHolder> Number(IEnumerable<ListHolder> holders)
    {
        ArgumentNullException.ThrowIfNull(holders);
        return [.. holders
            .OrderBy(h => h.Slot is null)
            .ThenBy(h => h.Slot ?? TimeOnly.MinValue)
            .ThenBy(h => h.ComparsaName, SpanishOrder.Names)
            .ThenBy(h => h.ComparsaId)
            .ThenBy(h => h.LastName, SpanishOrder.Names)
            .ThenBy(h => h.FirstName, SpanishOrder.Names)
            .ThenBy(h => h.EntryId)
            .Select((holder, index) => new NumberedHolder(index + 1, holder))];
    }
}
