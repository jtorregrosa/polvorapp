namespace PolvorApp.IdentityAccess.Contracts;

/// <summary>
/// The comparsas a FiringChief is assigned to. Implemented by the federation-catalog module
/// (change #4); until then the identity module's default returns none (deny by default, BR-12).
/// </summary>
public interface IFiringChiefAssignmentSource
{
    Task<IReadOnlySet<Guid>> GetComparsaIdsAsync(Guid userId, CancellationToken cancellationToken);
}
