using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ComplianceInsights.Contracts;

namespace PolvorApp.ComparsaOrders.Entries;

/// <summary>
/// The history copy of an entry (spec: Entry history (BR-14); design D3): taken from the registry while
/// the arquebusier and the weapon are in it, and kept as it is once they leave.
/// </summary>
internal static class EntryCopies
{
    /// <summary>
    /// Refreshes the identity and owned-weapon copy from <paramref name="arquebusier"/>, the entry's
    /// arquebusier. <c>CopiedAt</c> moves only when a copied value changes, so an unchanged entry keeps its
    /// row version. Returns false, changing nothing, when the entry's owned weapon is not among the
    /// arquebusier's: it left the registry after the entry was read.
    /// </summary>
    public static bool TryRefresh(EditionEntry entry, RosterArquebusier arquebusier, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(arquebusier);
        RosterWeapon? weapon = null;
        if (entry is { WeaponSource: WeaponSource.Owned, OwnedWeaponId: { } weaponId })
        {
            weapon = arquebusier.Weapons.SingleOrDefault(w => w.Id == weaponId);
            if (weapon is null)
            {
                return false;
            }
        }

        var before = Copy(entry);
        (entry.FirstName, entry.LastName, entry.NationalId, entry.FederationId) =
            (arquebusier.FirstName, arquebusier.LastName, arquebusier.NationalId, arquebusier.FederationId);
        if (entry.WeaponSource != WeaponSource.Owned)
        {
            ClearOwnedWeapon(entry);
        }
        else if (weapon is not null)
        {
            (entry.OwnedWeaponModelId, entry.OwnedWeaponNumber, entry.OwnedWeaponGuideNumber) =
                (weapon.WeaponModelId, weapon.WeaponNumber, weapon.OwnershipGuideNumber);
        }

        if (Copy(entry) != before)
        {
            entry.CopiedAt = now;
        }

        return true;
    }

    /// <summary>Like <see cref="TryRefresh"/>, for callers whose arquebusier was read with the entry's weapon checked against it.</summary>
    public static void Refresh(EditionEntry entry, RosterArquebusier arquebusier, DateTimeOffset now)
    {
        // The input rules and the pre-fill only name the arquebusier's own weapons.
        if (!TryRefresh(entry, arquebusier, now))
        {
            throw new InvalidOperationException($"Entry {entry.Id} names an owned weapon its arquebusier does not have.");
        }
    }

    private static (string?, string?, string?, int?, Guid?, Guid?, string?, string?) Copy(EditionEntry entry) =>
        (entry.FirstName, entry.LastName, entry.NationalId, entry.FederationId,
         entry.OwnedWeaponId, entry.OwnedWeaponModelId, entry.OwnedWeaponNumber, entry.OwnedWeaponGuideNumber);

    /// <summary>Drops the owned-weapon copy when the entry no longer uses an owned weapon.</summary>
    public static void ClearOwnedWeapon(EditionEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        (entry.OwnedWeaponId, entry.OwnedWeaponModelId, entry.OwnedWeaponNumber, entry.OwnedWeaponGuideNumber) = (null, null, null, null);
    }

    /// <summary>What the compliance rules read about the arquebusier (BR-04).</summary>
    public static ComplianceFacts FactsOf(RosterArquebusier arquebusier)
    {
        ArgumentNullException.ThrowIfNull(arquebusier);
        return new ComplianceFacts(
            arquebusier.BirthDate,
            arquebusier.License switch
            {
                null => null,
                ArquebusierLicenseFacts.Pending => new ComplianceLicense.Pending(),
                ArquebusierLicenseFacts.Issued issued => new ComplianceLicense.Issued(issued.ExpiresOn, issued.HasFrontPhoto, issued.HasBackPhoto),
                _ => throw new InvalidOperationException($"Unknown license shape of arquebusier {arquebusier.Id}."),
            },
            arquebusier.TrainingCompletedOn,
            arquebusier.HasIdPhoto);
    }
}
