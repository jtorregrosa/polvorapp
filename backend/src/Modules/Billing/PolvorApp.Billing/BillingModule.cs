using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Billing.Contracts;
using PolvorApp.SharedKernel.Modules;

namespace PolvorApp.Billing;

/// <summary>
/// Capability billing (UC-28): the amount a comparsa owes the Federation for an edition. It owns the
/// pricing rule, not data: no schema, no migrations and no endpoints. The orders call it and return
/// the billing with the order data it is derived from (change add-billing-summary, design D1).
/// </summary>
public sealed class BillingModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration) =>
        services.AddSingleton<IBillingCalculator, BillingCalculator>();

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
    }
}
