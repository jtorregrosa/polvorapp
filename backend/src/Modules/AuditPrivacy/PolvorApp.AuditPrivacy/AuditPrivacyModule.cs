using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PolvorApp.AuditPrivacy.Persistence;
using PolvorApp.SharedKernel.Auditing;
using PolvorApp.SharedKernel.Modules;
using PolvorApp.SharedKernel.Persistence;

namespace PolvorApp.AuditPrivacy;

/// <summary>
/// Capability audit-privacy (SEC-05, UC-25, UC-26). This change adds only recording: the audit
/// table and <see cref="IAuditTrail"/>; the viewer and GDPR requests arrive in change #15.
/// </summary>
public sealed class AuditPrivacyModule : IModule
{
    /// <summary>The audit table must exist before any module that writes to it is migrated.</summary>
    public const int MigrationOrder = 0;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.TryAddSingleton(TimeProvider.System);
        services.AddModuleDbContext<AuditDbContext>(AuditTrailModel.Schema, MigrationOrder);
        services.AddSingleton<IAuditTrail, AuditTrail>();
        services.AddScoped<IAuditLog, AuditLog>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
    }
}
