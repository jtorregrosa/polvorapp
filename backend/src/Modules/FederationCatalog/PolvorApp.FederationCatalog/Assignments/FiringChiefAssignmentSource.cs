using Microsoft.EntityFrameworkCore;
using PolvorApp.FederationCatalog.Persistence;
using PolvorApp.IdentityAccess.Contracts;

namespace PolvorApp.FederationCatalog.Assignments;

/// <summary>
/// The comparsas a FiringChief may access (BR-12, spec "Assignments determine the comparsa scope").
/// Read on every request that needs the scope, so assignment changes apply on the next request.
/// Replaces the identity module's deny-all default (design D1).
/// </summary>
internal sealed class FiringChiefAssignmentSource(FederationCatalogDbContext db) : IFiringChiefAssignmentSource
{
    public async Task<IReadOnlySet<Guid>> GetComparsaIdsAsync(Guid userId, CancellationToken cancellationToken) =>
        (await db.Assignments.AsNoTracking().Where(a => a.UserId == userId).Select(a => a.ComparsaId).ToListAsync(cancellationToken)).ToHashSet();
}
