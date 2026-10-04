using System.Reflection;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using PolvorApp.AuditPrivacy.Contracts;
using PolvorApp.AuditPrivacy.Persistence;
using PolvorApp.AuditPrivacy.Privacy;
using PolvorApp.AuditPrivacy.Retention;
using PolvorApp.AuditPrivacy.Viewer;
using PolvorApp.SharedKernel.Auditing;
using PolvorApp.SharedKernel.Modules;
using PolvorApp.SharedKernel.Persistence;

namespace PolvorApp.AuditPrivacy;

/// <summary>
/// Capability audit-privacy (SEC-05, UC-25, UC-26): the audit table and <see cref="IAuditTrail"/>,
/// the action catalogue, the retention purge, the audit log and the GDPR requests.
/// </summary>
public sealed class AuditPrivacyModule : IModule
{
    /// <summary>The audit table must exist before any module that writes to it is migrated.</summary>
    public const int MigrationOrder = 0;

    private static readonly bool IsBuildTimeDocumentGeneration = Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.TryAddSingleton(TimeProvider.System);
        services.AddModuleDbContext<AuditDbContext>(AuditTrailModel.Schema, MigrationOrder);
        services.AddAuditActions(AuditPrivacyAuditActions.All);
        services.AddSingleton<AuditActionCatalog>();
        services.AddSingleton<IAuditTrail, AuditTrail>();
        services.AddScoped<IAuditLog, AuditLog>();
        services.AddScoped<AuditQuery>();
        services.AddScoped<PersonalDataRequests>();
        services.AddScoped<PersonalDataPackager>();
        services.AddLocalization();
        services.AddScoped<IPersonalDataParticipant, AuditPersonalData>();

        services.AddSingleton<IValidateOptions<AuditRetentionOptions>, AuditRetentionOptionsValidator>();
        var retention = services.AddOptions<AuditRetentionOptions>().Configure(o => AuditRetentionOptions.Bind(o, configuration));
        services.AddScoped<AuditPurge>();
        services.AddSingleton<AuditRetentionService>();
        services.AddSingleton<IHostCommand, PurgeAuditCommand>();
        if (!IsBuildTimeDocumentGeneration)
        {
            retention.ValidateOnStart();
            services.AddHostedService<AuditActionCatalogCheck>();
            // Reads Audit:PurgeEnabled; test hosts turn it off and run the purge directly.
            services.AddHostedService(provider => provider.GetRequiredService<AuditRetentionService>());
        }
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapAuditLogEndpoints().MapPrivacyEndpoints();
}
