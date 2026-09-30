using PolvorApp.IdentityAccess.Contracts;

namespace PolvorApp.IdentityAccess.Security;

/// <summary>Until federation-catalog (#4) provides assignments, FiringChiefs reach no comparsa.</summary>
internal sealed class NoFiringChiefAssignments : IFiringChiefAssignmentSource
{
    public Task<IReadOnlySet<Guid>> GetComparsaIdsAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlySet<Guid>>(new HashSet<Guid>());
}
