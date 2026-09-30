using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace PolvorApp.SharedKernel.Modules;

/// <summary>Registration of backend modules in the API host.</summary>
public static class ModuleExtensions
{
    /// <summary>
    /// Registers the services of each module and keeps the list for endpoint mapping.
    /// Call it once, with every module.
    /// </summary>
    public static IServiceCollection AddModules(
        this IServiceCollection services, IConfiguration configuration, params IModule[] modules)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(modules);
        if (services.Any(d => d.ServiceType == typeof(IReadOnlyList<IModule>)))
        {
            throw new InvalidOperationException($"{nameof(AddModules)} must be called only once, with every module.");
        }

        foreach (var module in modules)
        {
            module.AddServices(services, configuration);
        }

        services.AddSingleton<IReadOnlyList<IModule>>(modules);
        return services;
    }

    /// <summary>Maps the endpoints of every module registered with <see cref="AddModules"/>.</summary>
    public static IEndpointRouteBuilder MapModules(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        foreach (var module in endpoints.ServiceProvider.GetRequiredService<IReadOnlyList<IModule>>())
        {
            module.MapEndpoints(endpoints);
        }

        return endpoints;
    }
}
