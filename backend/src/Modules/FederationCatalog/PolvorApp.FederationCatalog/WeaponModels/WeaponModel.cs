using PolvorApp.FederationCatalog.Contracts;

namespace PolvorApp.FederationCatalog.WeaponModels;

/// <summary>
/// A catalogue entry (spec: Weapon models (BR-07)). Side, handedness and size are required for trabucos
/// and arcabuces and optional for pistols; any kind may be rentable.
/// </summary>
internal sealed class WeaponModel
{
    public const int LabelMaxLength = 100;

    public required Guid Id { get; init; }

    public required WeaponKind Kind { get; set; }

    public Side? Side { get; set; }

    public Handedness? Handedness { get; set; }

    public WeaponSize? Size { get; set; }

    public bool Rentable { get; set; }

    public required string Label { get; set; }

    public bool Active { get; set; } = true;

    public required DateTimeOffset CreatedAt { get; init; }
}
