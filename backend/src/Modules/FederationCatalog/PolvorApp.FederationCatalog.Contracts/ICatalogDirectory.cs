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
/// <param name="Rentable">Whether editions may offer it for rental; never for a pistol (BR-07; add-festival-editions).</param>
public sealed record WeaponModelSummary(
    Guid Id, WeaponKind Kind, Side? Side, Handedness? Handedness, WeaponSize? Size, string Label, bool Active, bool Rentable);

/// <summary>A FiringChief assigned to a comparsa (add-notifications, design D6).</summary>
/// <param name="UserId">The FiringChief's user identifier.</param>
/// <param name="ComparsaId">The assigned comparsa.</param>
public sealed record FiringChiefAssignmentSummary(Guid UserId, Guid ComparsaId);

/// <summary>
/// A comparsa's logo or the Federation's, for other modules, e.g. to print it in a document (change
/// add-comparsa-logos, design D6; add-distribution-planning, design D11).
/// </summary>
/// <param name="Png">The whole image, PNG-encoded with its transparency.</param>
/// <param name="Width">Width in pixels, at most 1024.</param>
/// <param name="Height">Height in pixels, at most 1024.</param>
public sealed record LogoImage(ReadOnlyMemory<byte> Png, int Width, int Height);

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

    /// <summary>The active comparsas, in no particular order (e.g. the comparsas expected to order; add-comparsa-orders).</summary>
    Task<IReadOnlyList<ComparsaSummary>> ListActiveComparsasAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The FiringChief assignments to active comparsas, in no particular order, e.g. to find the
    /// recipients of a comparsa's notifications (add-notifications). The user's role and status are not
    /// checked here: callers read them from the identity module.
    /// </summary>
    Task<IReadOnlyList<FiringChiefAssignmentSummary>> ListFiringChiefAssignmentsAsync(CancellationToken cancellationToken);

    /// <summary>The weapon model, or null when none has that id.</summary>
    Task<WeaponModelSummary?> FindWeaponModelAsync(Guid weaponModelId, CancellationToken cancellationToken);

    /// <summary>The weapon models that exist among <paramref name="weaponModelIds"/>, each once and in no particular order.</summary>
    Task<IReadOnlyList<WeaponModelSummary>> FindWeaponModelsAsync(IReadOnlyCollection<Guid> weaponModelIds, CancellationToken cancellationToken);

    /// <summary>
    /// The comparsa's logo, or null when the comparsa or its logo does not exist. Throws
    /// <c>StorageUnavailableException</c> when the storage cannot be reached; a document can then
    /// fall back to a placeholder.
    /// </summary>
    Task<LogoImage?> ReadComparsaLogoAsync(Guid comparsaId, CancellationToken cancellationToken);

    /// <summary>
    /// The Federation's logo for documents (add-distribution-planning, design D11), or null until an Admin
    /// uploads it. Throws <c>StorageUnavailableException</c> when the storage cannot be reached: the
    /// document then fails rather than print without it (spec: Federation logo).
    /// </summary>
    Task<LogoImage?> ReadFederationLogoAsync(CancellationToken cancellationToken);
}
