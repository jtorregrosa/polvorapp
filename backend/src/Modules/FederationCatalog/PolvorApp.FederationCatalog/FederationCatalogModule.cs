using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PolvorApp.FederationCatalog.Assignments;
using PolvorApp.FederationCatalog.Comparsas;
using PolvorApp.FederationCatalog.Endpoints;
using PolvorApp.FederationCatalog.Persistence;
using PolvorApp.FederationCatalog.Seeding;
using PolvorApp.FederationCatalog.WeaponModels;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.SharedKernel.Modules;
using PolvorApp.SharedKernel.Persistence;
using PolvorApp.SharedKernel.Seeding;

namespace PolvorApp.FederationCatalog;

/// <summary>
/// Capability federation-catalog (UC-24 catalogue, BR-07, BR-12): comparsas, FiringChief
/// assignments that feed the comparsa scope, and the weapon model catalogue.
/// </summary>
public sealed class FederationCatalogModule : IModule
{
    /// <summary>After identity (10): assignments refer to users, and the context writes audit entries.</summary>
    public const int MigrationOrder = 20;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.TryAddSingleton(TimeProvider.System);
        services.AddModuleDbContext<FederationCatalogDbContext>(FederationCatalogDbContext.Schema, MigrationOrder);
        services.AddScoped<ComparsaAdministration>();
        services.AddScoped<AssignmentAdministration>();
        services.AddScoped<WeaponModelAdministration>();
        services.AddScoped<IDataSeeder, CatalogSeeder>();

        // Replaces the identity module's deny-all default, whatever the module order (design D1).
        services.Replace(ServiceDescriptor.Scoped<IFiringChiefAssignmentSource, FiringChiefAssignmentSource>());
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapComparsaEndpoints();
        endpoints.MapAssignmentEndpoints();
        endpoints.MapWeaponModelEndpoints();
    }
}
