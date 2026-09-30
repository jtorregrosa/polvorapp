using PolvorApp.IdentityAccess.Contracts;

namespace PolvorApp.IdentityAccess.Security;

/// <summary>BR-12: Admins reach every comparsa; FiringChiefs only their assignments; everyone else none.</summary>
internal sealed class ComparsaScope(ICurrentUser currentUser, IFiringChiefAssignmentSource assignments) : IComparsaScope
{
    private ComparsaAccess? _access;

    public async Task<ComparsaAccess> GetAccessAsync(CancellationToken cancellationToken)
    {
        if (_access is not null)
        {
            return _access;
        }

        _access = currentUser switch
        {
            { UserId: null } => ComparsaAccess.None,
            { Role: UserRole.Admin } => ComparsaAccess.All,
            { Role: UserRole.FiringChief, UserId: { } id } => ComparsaAccess.Only(await assignments.GetComparsaIdsAsync(id, cancellationToken)),
            _ => ComparsaAccess.None,
        };
        return _access;
    }
}
