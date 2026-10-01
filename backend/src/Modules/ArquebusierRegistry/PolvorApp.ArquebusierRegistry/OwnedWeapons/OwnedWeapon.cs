namespace PolvorApp.ArquebusierRegistry.OwnedWeapons;

/// <summary>A weapon owned by an arquebusier, with its ownership guide (spec: Owned weapons, UC-04).</summary>
internal sealed class OwnedWeapon
{
    public const int NumberMaxLength = 30;

    public required Guid Id { get; init; }

    public required Guid ArquebusierId { get; init; }

    /// <summary>A catalogue model of any kind, pistols included.</summary>
    public required Guid WeaponModelId { get; set; }

    /// <summary>Engraved on the stock; not unique (numbers of different makers can coincide).</summary>
    public required string WeaponNumber { get; set; }

    /// <summary>Stored upper-cased; unique across the Federation.</summary>
    public required string OwnershipGuideNumber { get; set; }

    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>PostgreSQL <c>xmin</c>, as for arquebusiers.</summary>
    public uint Version { get; set; }
}
