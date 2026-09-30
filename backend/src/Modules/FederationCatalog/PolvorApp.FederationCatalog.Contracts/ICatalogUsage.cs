namespace PolvorApp.FederationCatalog.Contracts;

/// <summary>
/// Implemented by modules whose records reference comparsas or weapon models (arquebusiers,
/// owned weapons, edition availability, orders). The catalog asks every implementation before a
/// deletion and refuses it while any reports a use (change add-federation-catalog, design D10).
/// </summary>
public interface ICatalogUsage
{
    /// <summary>True when records of the implementing module reference the comparsa.</summary>
    Task<bool> IsComparsaInUseAsync(Guid comparsaId, CancellationToken cancellationToken);

    /// <summary>True when records of the implementing module reference the weapon model.</summary>
    Task<bool> IsWeaponModelInUseAsync(Guid weaponModelId, CancellationToken cancellationToken);
}
