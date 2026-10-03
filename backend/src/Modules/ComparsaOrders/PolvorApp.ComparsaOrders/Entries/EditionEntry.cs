using PolvorApp.ArquebusierRegistry.Contracts;

namespace PolvorApp.ComparsaOrders.Entries;

/// <summary>
/// One arquebusier's participation in an edition (spec: Edition entries (BR-05, BR-07)). It links to
/// the registry while the arquebusier and their weapon exist, and keeps its own copy of what it needs
/// so it still reads correctly once they leave (spec: Entry history (BR-14); design D3).
/// </summary>
internal sealed class EditionEntry
{
    public const int MaxPowderKg = 2;
    public const int MaxCapsBoxes = 99;
    public const int NameMaxLength = 100;
    public const int NationalIdLength = 9;
    public const int WeaponNumberMaxLength = 30;

    public required Guid Id { get; init; }

    public required Guid OrderId { get; init; }

    /// <summary>Denormalised from the order for the "one entry per arquebusier per edition" index.</summary>
    public required Guid EditionId { get; init; }

    /// <summary>Null once the arquebusier is deleted from the registry (<c>ON DELETE SET NULL</c>).</summary>
    public Guid? ArquebusierId { get; set; }

    /// <summary>The status for this edition, independent of the registry after it is created.</summary>
    public ArquebusierStatus Status { get; set; }

    public int PowderKg { get; set; }

    public int CapsBoxes { get; set; }

    public CapsType? CapsType { get; set; }

    public WeaponSource WeaponSource { get; set; } = WeaponSource.None;

    /// <summary>The owned weapon for <see cref="WeaponSource.Owned"/>; null once it is removed from the registry.</summary>
    public Guid? OwnedWeaponId { get; set; }

    /// <summary>The model offered for rental, for <see cref="WeaponSource.Rental"/> (BR-07).</summary>
    public Guid? RentalWeaponModelId { get; set; }

    public FlaskOption Flask { get; set; } = FlaskOption.None;

    public required DateTimeOffset CreatedAt { get; init; }

    // The history copy (design D3). Nullable only so a GDPR erasure (#15) can blank it.
    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    public string? NationalId { get; set; }

    public int? FederationId { get; set; }

    public Guid? OwnedWeaponModelId { get; set; }

    public string? OwnedWeaponNumber { get; set; }

    public string? OwnedWeaponGuideNumber { get; set; }

    /// <summary>When the copy was last taken from the registry.</summary>
    public required DateTimeOffset CopiedAt { get; set; }

    /// <summary>PostgreSQL <c>xmin</c>: an edit based on an older version is rejected (<c>entries.modified</c>).</summary>
    public uint Version { get; set; }
}
