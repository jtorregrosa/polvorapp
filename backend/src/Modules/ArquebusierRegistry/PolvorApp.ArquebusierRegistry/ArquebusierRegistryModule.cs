using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PolvorApp.ArquebusierRegistry.Arquebusiers;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ArquebusierRegistry.Endpoints;
using PolvorApp.ArquebusierRegistry.Import;
using PolvorApp.ArquebusierRegistry.Insights;
using PolvorApp.ArquebusierRegistry.Lock;
using PolvorApp.ArquebusierRegistry.OwnedWeapons;
using PolvorApp.ArquebusierRegistry.Persistence;
using PolvorApp.ArquebusierRegistry.Photos;
using PolvorApp.ArquebusierRegistry.Privacy;
using PolvorApp.ArquebusierRegistry.Seeding;
using PolvorApp.AuditPrivacy.Contracts;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.SharedKernel.Auditing;
using PolvorApp.SharedKernel.Modules;
using PolvorApp.SharedKernel.Persistence;
using PolvorApp.SharedKernel.Seeding;
using PolvorApp.SharedKernel.Storage;

namespace PolvorApp.ArquebusierRegistry;

/// <summary>
/// Capability arquebusier-registry (UC-01..05, UC-09, UC-29; BR-01..03, BR-12..14): arquebusiers,
/// their current license and training course, owned weapons, photos, transfers, deletion and the
/// spreadsheet import.
/// </summary>
public sealed class ArquebusierRegistryModule : IModule
{
    /// <summary>After the catalog (20): registry tables reference comparsas and weapon models by foreign key.</summary>
    public const int MigrationOrder = 30;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddAuditActions(ArquebusierRegistryAuditActions.All);
        services.AddAuditRecordResolver<ArquebusierRegistryDbContext, Arquebusiers.Arquebusier>(ArquebusierAdministration.EntityType);
        services.AddHttpContextAccessor();
        services.TryAddSingleton(TimeProvider.System);
        services.AddModuleDbContext<ArquebusierRegistryDbContext>(ArquebusierRegistryDbContext.Schema, MigrationOrder);

        // One of possibly several vetoes the catalog asks before a deletion: added, never replaced.
        services.AddScoped<ICatalogUsage, RegistryCatalogUsage>();
        services.AddScoped<RegistryWriteGuard>();
        services.AddScoped<ArquebusierAdministration>();
        services.AddScoped<IPersonalDataParticipant, RegistryPersonalData>();
        services.AddScoped<OwnedWeaponAdministration>();
        services.AddScoped<ArquebusierPhotoAdministration>();
        services.AddScoped<PhotoObjects>();
        services.AddScoped<PhotoReader>();
        services.AddScoped<IIdPhotoReader, RegistryIdPhotoReader>();

        // One of possibly several owners the platform's orphan sweep asks (design D2): added, never replaced.
        services.AddScoped<IStoredObjectOwner, RegistryPhotoOwner>();
        services.AddScoped<IDataSeeder, RegistrySeeder>();
        services.AddScoped<ArquebusierViews>();
        services.AddScoped<ArquebusierQueries>();
        services.AddScoped<ArquebusierImporter>();
        services.AddScoped<RegistryLockAdministration>();
        services.AddSingleton<ImportSlots>();

        // Read contract for the compliance insights (design D3).
        services.AddScoped<IArquebusierFacts, RegistryArquebusierFacts>();

        // Read contract for comparsa orders (add-comparsa-orders, design D5).
        services.AddScoped<IArquebusierRoster, RegistryArquebusierRoster>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapArquebusierEndpoints();
        endpoints.MapOwnedWeaponEndpoints();
        endpoints.MapPhotoEndpoints();
        endpoints.MapImportEndpoints();
        endpoints.MapRegistryLockEndpoints();
    }
}
