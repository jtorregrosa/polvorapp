using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PolvorApp.ArquebusierRegistry.Arquebusiers;
using PolvorApp.ArquebusierRegistry.Endpoints;
using PolvorApp.ArquebusierRegistry.OwnedWeapons;
using PolvorApp.ArquebusierRegistry.Persistence;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.SharedKernel.Modules;
using PolvorApp.SharedKernel.Persistence;

namespace PolvorApp.ArquebusierRegistry;

/// <summary>
/// Capability arquebusier-registry (UC-01..05, UC-29; BR-01..03, BR-12..14): arquebusiers, their
/// current license and training course, owned weapons, transfers and deletion.
/// </summary>
public sealed class ArquebusierRegistryModule : IModule
{
    /// <summary>After the catalog (20): registry tables reference comparsas and weapon models by foreign key.</summary>
    public const int MigrationOrder = 30;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.TryAddSingleton(TimeProvider.System);
        services.AddModuleDbContext<ArquebusierRegistryDbContext>(ArquebusierRegistryDbContext.Schema, MigrationOrder);

        // One of possibly several vetoes the catalog asks before a deletion: added, never replaced.
        services.AddScoped<ICatalogUsage, RegistryCatalogUsage>();
        services.AddScoped<RegistryWriteGuard>();
        services.AddScoped<ArquebusierAdministration>();
        services.AddScoped<OwnedWeaponAdministration>();
        services.AddScoped<ArquebusierViews>();
        services.AddScoped<ArquebusierQueries>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapArquebusierEndpoints();
        endpoints.MapOwnedWeaponEndpoints();
    }
}
