using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ComparsaOrders.Contracts;

namespace PolvorApp.ComparsaOrders.Entries;

/// <summary>
/// The values of a new entry (spec: Pre-fill from the previous edition (UC-12, BR-11); design D8). The
/// status comes from the registry; an <c>ACTIVE</c> previous entry gives its choices, never a quantity
/// carried over, and a loan is never copied.
/// </summary>
internal static class Prefill
{
    /// <param name="registryStatus">The arquebusier's status in the registry.</param>
    /// <param name="ownedWeaponIds">The arquebusier's owned weapons now.</param>
    /// <param name="previous">The entry in the latest earlier edition in which they have one, or null.</param>
    /// <param name="offeredModelIds">The models offered for rental in the new edition (BR-07).</param>
    public static EntryValues For(
        ArquebusierStatus registryStatus,
        IReadOnlyCollection<Guid> ownedWeaponIds,
        EntryValues? previous,
        IReadOnlySet<Guid> offeredModelIds)
    {
        ArgumentNullException.ThrowIfNull(ownedWeaponIds);
        ArgumentNullException.ThrowIfNull(offeredModelIds);
        if (registryStatus == ArquebusierStatus.Reserve)
        {
            return EntryValues.Empty(ArquebusierStatus.Reserve);
        }

        if (previous is not { Status: ArquebusierStatus.Active })
        {
            return ownedWeaponIds.Count == 1
                ? EntryValues.Empty(ArquebusierStatus.Active) with { WeaponSource = WeaponSource.Owned, OwnedWeaponId = ownedWeaponIds.Single() }
                : EntryValues.Empty(ArquebusierStatus.Active);
        }

        var (source, owned, rental) = previous switch
        {
            { WeaponSource: WeaponSource.Owned, OwnedWeaponId: { } weapon } when ownedWeaponIds.Contains(weapon) =>
                (WeaponSource.Owned, (Guid?)weapon, (Guid?)null),
            { WeaponSource: WeaponSource.Rental, RentalWeaponModelId: { } model } when offeredModelIds.Contains(model) =>
                (WeaponSource.Rental, null, model),
            _ => (WeaponSource.None, null, null),
        };
        return previous with { Status = ArquebusierStatus.Active, WeaponSource = source, OwnedWeaponId = owned, RentalWeaponModelId = rental };
    }
}
