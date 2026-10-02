using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PolvorApp.ComplianceInsights.Contracts;
using PolvorApp.ComplianceInsights.Endpoints;
using PolvorApp.SharedKernel.Modules;

namespace PolvorApp.ComplianceInsights;

/// <summary>
/// Capability compliance-insights (UC-06, UC-07; BR-04 as warnings, BR-12): the compliance warnings
/// derived from the registry, the warning summary and the statistics. It owns rules, not data, so
/// it has no schema and no migrations (change add-compliance-insights, design D1).
/// </summary>
public sealed class ComplianceInsightsModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IComplianceRules, ComplianceRules>();
        services.AddScoped<ScopedFacts>();
        services.AddScoped<ComplianceInsightsQueries>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapComplianceEndpoints();
}
