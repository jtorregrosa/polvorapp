using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.ArquebusierRegistry.Persistence;
using PolvorApp.AuditPrivacy.Persistence;
using PolvorApp.FederationCatalog.Persistence;
using PolvorApp.FestivalEditions.Persistence;
using PolvorApp.IdentityAccess.Persistence;

namespace PolvorApp.Api.Tests.Persistence;

/// <summary>A model change without its migration fails here, not at the next <c>migrate</c>.</summary>
public sealed class ModelDriftTests
{
    [Fact]
    public async Task Every_module_context_has_a_migration_for_its_current_model()
    {
        await using var factory = new ApiFactory("Host=offline");
        await using var scope = factory.Services.CreateAsyncScope();

        Assert.False(scope.ServiceProvider.GetRequiredService<AuditDbContext>().Database.HasPendingModelChanges(), nameof(AuditDbContext));
        Assert.False(scope.ServiceProvider.GetRequiredService<IdentityAccessDbContext>().Database.HasPendingModelChanges(), nameof(IdentityAccessDbContext));
        Assert.False(scope.ServiceProvider.GetRequiredService<FederationCatalogDbContext>().Database.HasPendingModelChanges(), nameof(FederationCatalogDbContext));
        Assert.False(scope.ServiceProvider.GetRequiredService<ArquebusierRegistryDbContext>().Database.HasPendingModelChanges(), nameof(ArquebusierRegistryDbContext));
        Assert.False(scope.ServiceProvider.GetRequiredService<FestivalEditionsDbContext>().Database.HasPendingModelChanges(), nameof(FestivalEditionsDbContext));
    }
}
