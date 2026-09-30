using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace PolvorApp.SharedKernel.Modules;

/// <summary>
/// Entry point of a backend module (one per OpenSpec capability, ADR-0001). The API host
/// registers every module explicitly in <c>Program.cs</c>.
/// </summary>
public interface IModule
{
    /// <summary>Registers the module's services.</summary>
    void AddServices(IServiceCollection services, IConfiguration configuration);

    /// <summary>Maps the module's HTTP endpoints under the API route group.</summary>
    void MapEndpoints(IEndpointRouteBuilder endpoints);
}
