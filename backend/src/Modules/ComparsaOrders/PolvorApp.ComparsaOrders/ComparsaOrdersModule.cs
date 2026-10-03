using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.ComparsaOrders.Endpoints;
using PolvorApp.ComparsaOrders.Entries;
using PolvorApp.ComparsaOrders.History;
using PolvorApp.ComparsaOrders.Loans;
using PolvorApp.ComparsaOrders.Orders;
using PolvorApp.ComparsaOrders.Persistence;
using PolvorApp.ComparsaOrders.Seeding;
using PolvorApp.ComparsaOrders.Totals;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.FestivalEditions.Contracts;
using PolvorApp.SharedKernel.Modules;
using PolvorApp.SharedKernel.Persistence;
using PolvorApp.SharedKernel.Seeding;

namespace PolvorApp.ComparsaOrders;

/// <summary>
/// Capability comparsa-orders (UC-12 to UC-16; BR-04, BR-05, BR-07, BR-09 to BR-14): comparsa orders
/// with one entry per arquebusier, weapon loans, submission, review and the Federation dashboard.
/// </summary>
public sealed class ComparsaOrdersModule : IModule
{
    /// <summary>After the editions (40): orders reference editions, comparsas, models and arquebusiers by foreign key.</summary>
    public const int MigrationOrder = 50;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.TryAddSingleton(TimeProvider.System);
        services.AddModuleDbContext<ComparsaOrdersDbContext>(ComparsaOrdersDbContext.Schema, MigrationOrder);
        services.AddScoped<OrderWriteGuard>();
        services.AddScoped<OrderGate>();
        services.AddScoped<OrderAdministration>();
        services.AddScoped<OrderViews>();
        services.AddScoped<EntryHistory>();
        services.AddScoped<LoanWriter>();
        services.AddScoped<LenderLookup>();
        services.AddScoped<OrderLifecycle>();
        services.AddScoped<OrderOverview>();
        services.AddScoped<EntryAdministration>();
        services.AddScoped<IDataSeeder, OrderSeeder>();

        // Vetoes other modules ask before a deletion: added, never replaced.
        services.AddScoped<IEditionUsage, OrdersEditionUsage>();
        services.AddScoped<ICatalogUsage, OrdersCatalogUsage>();

        // Acts inside the registry's deletion of an arquebusier (design D3).
        services.AddScoped<IArquebusierDeletionParticipant, OrdersDeletionParticipant>();

        // Read by the registry and the compliance insights (design D5).
        services.AddScoped<IParticipationHistory, ParticipationHistory>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapOrderEndpoints();
}
