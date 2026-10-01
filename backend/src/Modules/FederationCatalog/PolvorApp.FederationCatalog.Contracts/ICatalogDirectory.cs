namespace PolvorApp.FederationCatalog.Contracts;

/// <summary>A comparsa as other modules see it.</summary>
/// <param name="Id">Comparsa identifier.</param>
/// <param name="Name">Display name.</param>
/// <param name="Side">Side the comparsa belongs to.</param>
/// <param name="Active">False once an Admin deactivated it: it keeps its history but takes nothing new.</param>
public sealed record ComparsaSummary(Guid Id, string Name, Side Side, bool Active);

/// <summary>A weapon model as other modules see it. Pistols may have no side, handedness or size.</summary>
/// <param name="Id">Weapon model identifier.</param>
/// <param name="Kind">Kind of weapon.</param>
/// <param name="Side">Side, optional for pistols.</param>
/// <param name="Handedness">Handedness, optional for pistols.</param>
/// <param name="Size">Size, optional for pistols.</param>
/// <param name="Label">Federation label, as entered.</param>
/// <param name="Active">False once an Admin deactivated it.</param>
public sealed record WeaponModelSummary(
    Guid Id, WeaponKind Kind, Side? Side, Handedness? Handedness, WeaponSize? Size, string Label, bool Active);

/// <summary>
/// Read-only lookup of comparsas and weapon models for other modules, e.g. to validate and show
/// arquebusiers and owned weapons (change add-arquebusier-registry, design D2). Inactive records are
/// included so callers can reject or still show them. It applies no comparsa scope: callers enforce
/// BR-12 themselves.
/// </summary>
public interface ICatalogDirectory
{
    /// <summary>The comparsa, or null when none has that id.</summary>
    Task<ComparsaSummary?> FindComparsaAsync(Guid comparsaId, CancellationToken cancellationToken);

    /// <summary>The comparsas that exist among <paramref name="comparsaIds"/>, each once and in no particular order.</summary>
    Task<IReadOnlyList<ComparsaSummary>> FindComparsasAsync(IReadOnlyCollection<Guid> comparsaIds, CancellationToken cancellationToken);

    /// <summary>The weapon model, or null when none has that id.</summary>
    Task<WeaponModelSummary?> FindWeaponModelAsync(Guid weaponModelId, CancellationToken cancellationToken);

    /// <summary>The weapon models that exist among <paramref name="weaponModelIds"/>, each once and in no particular order.</summary>
    Task<IReadOnlyList<WeaponModelSummary>> FindWeaponModelsAsync(IReadOnlyCollection<Guid> weaponModelIds, CancellationToken cancellationToken);
}
