using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.SharedKernel.Modules;

namespace PolvorApp.Api.Tests;

public sealed class ModuleRegistrationTests
{
    private sealed class RecordingModule : IModule
    {
        public bool ServicesAdded { get; private set; }

        public void AddServices(IServiceCollection services, IConfiguration configuration) => ServicesAdded = true;

        public void MapEndpoints(IEndpointRouteBuilder endpoints)
        {
        }
    }

    [Fact]
    public void AddModules_registers_the_services_of_each_module()
    {
        var module = new RecordingModule();

        new ServiceCollection().AddModules(new ConfigurationBuilder().Build(), module);

        Assert.True(module.ServicesAdded);
    }

    [Fact]
    public void AddModules_called_twice_fails_instead_of_silently_dropping_modules()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        services.AddModules(configuration, new RecordingModule());

        Assert.Throws<InvalidOperationException>(() => services.AddModules(configuration, new RecordingModule()));
    }
}
