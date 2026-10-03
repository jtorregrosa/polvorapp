using PolvorApp.ArquebusierRegistry.Contracts;

namespace PolvorApp.ComparsaOrders.Entries;

/// <summary>
/// The editable values of an edition entry (spec: Edition entries (BR-05, BR-07)), without its loan:
/// what the pre-fill produces, the input validates and the entity stores.
/// </summary>
internal sealed record EntryValues(
    ArquebusierStatus Status,
    int PowderKg,
    int CapsBoxes,
    CapsType? CapsType,
    WeaponSource WeaponSource,
    Guid? OwnedWeaponId,
    Guid? RentalWeaponModelId,
    FlaskOption Flask)
{
    /// <summary>No powder, caps, weapon or flask: the start of a first entry, and every <c>RESERVE</c> entry (BR-05).</summary>
    public static EntryValues Empty(ArquebusierStatus status) =>
        new(status, PowderKg: 0, CapsBoxes: 0, CapsType: null, WeaponSource.None, OwnedWeaponId: null, RentalWeaponModelId: null, FlaskOption.None);

    /// <summary>The values an entry stores.</summary>
    public static EntryValues Of(EditionEntry entry) =>
        new(entry.Status, entry.PowderKg, entry.CapsBoxes, entry.CapsType, entry.WeaponSource, entry.OwnedWeaponId, entry.RentalWeaponModelId, entry.Flask);

    /// <summary>Writes the values onto <paramref name="entry"/>.</summary>
    public void ApplyTo(EditionEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        entry.Status = Status;
        entry.PowderKg = PowderKg;
        entry.CapsBoxes = CapsBoxes;
        entry.CapsType = CapsType;
        entry.WeaponSource = WeaponSource;
        entry.OwnedWeaponId = OwnedWeaponId;
        entry.RentalWeaponModelId = RentalWeaponModelId;
        entry.Flask = Flask;
    }
}
