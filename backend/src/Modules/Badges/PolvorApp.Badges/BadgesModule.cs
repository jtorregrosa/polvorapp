using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PolvorApp.Badges.Documents;
using PolvorApp.Badges.Endpoints;
using PolvorApp.SharedKernel.Auditing;
using PolvorApp.SharedKernel.Modules;

namespace PolvorApp.Badges;

/// <summary>
/// Capability badges (UC-30; BR-04, BR-12): printable arquebusier badges for a comparsa or a selection,
/// for Admins (change add-badges). Derived documents only: the module stores nothing and has no schema.
/// </summary>
public sealed class BadgesModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddAuditActions(BadgesAuditActions.All);
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<BadgeSlots>();
        services.AddScoped<BadgeDocuments>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapBadgeEndpoints();
}
