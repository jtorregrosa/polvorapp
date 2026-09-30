using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.FederationCatalog.Assignments;
using PolvorApp.FederationCatalog.Persistence;

namespace PolvorApp.Api.Tests.Infrastructure;

/// <summary>Direct catalogue data access for tests that need state the API under test does not create.</summary>
public static class CatalogData
{
    public static async Task AssignAsync(this IdentityTestHost host, Guid comparsaId, Guid userId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FederationCatalogDbContext>();
        db.Assignments.Add(new FiringChiefAssignment { ComparsaId = comparsaId, UserId = userId, AssignedAt = host.Time.GetUtcNow() });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    public static async Task<List<Guid>> AssignedComparsasAsync(this IdentityTestHost host, Guid userId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FederationCatalogDbContext>();
        return await db.Assignments.AsNoTracking().Where(a => a.UserId == userId).Select(a => a.ComparsaId).ToListAsync(TestContext.Current.CancellationToken);
    }
}
