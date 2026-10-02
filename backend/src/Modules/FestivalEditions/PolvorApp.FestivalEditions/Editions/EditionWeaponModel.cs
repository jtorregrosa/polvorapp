namespace PolvorApp.FestivalEditions.Editions;

/// <summary>A catalogue weapon model in an edition's set of rental models (spec: Rental models offered in an edition (BR-07)).</summary>
internal sealed class EditionWeaponModel
{
    public required Guid EditionId { get; init; }

    public required Guid WeaponModelId { get; init; }
}
