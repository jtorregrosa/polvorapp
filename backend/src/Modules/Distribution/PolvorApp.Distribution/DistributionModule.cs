using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PolvorApp.Distribution.Days;
using PolvorApp.Distribution.Documents;
using PolvorApp.Distribution.Endpoints;
using PolvorApp.Distribution.Persistence;
using PolvorApp.Distribution.Proxies;
using PolvorApp.Distribution.Seeding;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.FestivalEditions.Contracts;
using PolvorApp.SharedKernel.Modules;
using PolvorApp.SharedKernel.Persistence;
using PolvorApp.SharedKernel.Seeding;

namespace PolvorApp.Distribution;

/// <summary>
/// Capability distribution (UC-18 to UC-20; BR-06, BR-12): distribution days with a slot per comparsa,
/// the exceptional pickup proxies and their authorisation forms, and the distribution lists with global
/// numbering (change add-distribution-planning).
/// </summary>
public sealed class DistributionModule : IModule
{
    /// <summary>After the orders (50): proxies reference edition entries, and days reference editions and comparsas, by foreign key.</summary>
    public const int MigrationOrder = 60;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.TryAddSingleton(TimeProvider.System);
        services.AddModuleDbContext<DistributionDbContext>(DistributionDbContext.Schema, MigrationOrder);

        services.AddScoped<DistributionWriteGuard>();
        services.AddScoped<DistributionDayAdministration>();
        services.AddScoped<DistributionViews>();
        services.AddScoped<ProxyAdministration>();
        services.AddScoped<ProxyViews>();
        services.AddScoped<DistributionDocuments>();
        services.AddScoped<IDataSeeder, DistributionSeeder>();

        // Vetoes other modules ask before a deletion: added, never replaced.
        services.AddScoped<IEditionUsage, DistributionEditionUsage>();
        services.AddScoped<ICatalogUsage, DistributionCatalogUsage>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapDistributionEndpoints();
}
