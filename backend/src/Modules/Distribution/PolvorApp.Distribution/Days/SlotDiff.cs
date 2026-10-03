using System.Globalization;

namespace PolvorApp.Distribution.Days;

/// <summary>One comparsa's slot change, as the audit records it: its previous and new time, <c>HH:mm</c> or null.</summary>
internal sealed record SlotChange(Guid ComparsaId, string? Previous, string? Current);

/// <summary>
/// What saving a set of slots changes against the stored set (spec: Distribution slots; design D8):
/// comparsas added, moved to another time and left out, and the audit's changes in comparsa id order.
/// Pure, so it is unit-tested without a database.
/// </summary>
internal sealed record SlotDiff(
    IReadOnlyList<SlotInput> Added,
    IReadOnlyList<SlotInput> Moved,
    IReadOnlyList<Guid> Removed,
    IReadOnlyList<SlotChange> Changes)
{
    public bool IsEmpty => Changes.Count == 0;

    /// <param name="stored">The stored slots, by comparsa.</param>
    /// <param name="requested">The requested set; each comparsa once (checked at the endpoint).</param>
    public static SlotDiff Compute(IReadOnlyDictionary<Guid, TimeOnly> stored, IReadOnlyList<SlotInput> requested)
    {
        ArgumentNullException.ThrowIfNull(stored);
        ArgumentNullException.ThrowIfNull(requested);
        var wanted = requested.ToDictionary(s => s.ComparsaId, s => s.StartsAt);
        var added = requested.Where(s => !stored.ContainsKey(s.ComparsaId)).ToList();
        var moved = requested.Where(s => stored.TryGetValue(s.ComparsaId, out var time) && time != s.StartsAt).ToList();
        var removed = stored.Keys.Where(id => !wanted.ContainsKey(id)).ToList();
        var changes = added.Select(s => new SlotChange(s.ComparsaId, null, Format(s.StartsAt)))
            .Concat(moved.Select(s => new SlotChange(s.ComparsaId, Format(stored[s.ComparsaId]), Format(s.StartsAt))))
            .Concat(removed.Select(id => new SlotChange(id, Format(stored[id]), null)))
            .OrderBy(c => c.ComparsaId)
            .ToList();
        return new SlotDiff(added, moved, removed, changes);
    }

    private static string Format(TimeOnly time) => time.ToString("HH:mm", CultureInfo.InvariantCulture);
}
