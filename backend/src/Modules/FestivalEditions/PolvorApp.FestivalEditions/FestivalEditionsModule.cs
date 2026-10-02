using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.FestivalEditions.Contracts;
using PolvorApp.FestivalEditions.Editions;
using PolvorApp.FestivalEditions.Endpoints;
using PolvorApp.FestivalEditions.Persistence;
using PolvorApp.FestivalEditions.Seeding;
using PolvorApp.SharedKernel.Modules;
using PolvorApp.SharedKernel.Persistence;
using PolvorApp.SharedKernel.Seeding;

namespace PolvorApp.FestivalEditions;

/// <summary>
/// Capability festival-editions (UC-10, UC-11; BR-07, BR-10, BR-12): festival editions with their
/// order window, prices, rental models and milestones, their lifecycle and the opening and closing
/// of their orders.
/// </summary>
public sealed class FestivalEditionsModule : IModule
{
    /// <summary>After the registry (30): edition tables reference catalogue weapon models by foreign key.</summary>
    public const int MigrationOrder = 40;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.TryAddSingleton(TimeProvider.System);
        services.AddModuleDbContext<FestivalEditionsDbContext>(FestivalEditionsDbContext.Schema, MigrationOrder);

        // One of possibly several vetoes the catalog asks before a deletion: added, never replaced.
        services.AddScoped<ICatalogUsage, FestivalEditionsCatalogUsage>();
        services.AddScoped<EditionWriteGuard>();
        services.AddScoped<EditionAdministration>();
        services.AddScoped<EditionLifecycle>();
        services.AddScoped<EditionWeaponModelAdministration>();
        services.AddScoped<CalendarMilestoneAdministration>();
        services.AddScoped<EditionViews>();
        services.AddScoped<IDataSeeder, EditionSeeder>();

        // Read contract for comparsa orders (#10), design D7.
        services.AddScoped<IEditionDirectory, EditionDirectory>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapEditionEndpoints();
        endpoints.MapCalendarMilestoneEndpoints();
    }
}
