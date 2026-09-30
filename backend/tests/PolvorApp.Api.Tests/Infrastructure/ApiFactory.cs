using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace PolvorApp.Api.Tests.Infrastructure;

/// <summary>
/// Hosts the API in memory. Settings are passed as environment-style configuration so tests
/// exercise the same configuration keys as the containers.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _environment;
    private readonly IReadOnlyDictionary<string, string?> _settings;
    private readonly Action<IServiceCollection>? _configureServices;

    public ApiFactory(
        string? connectionString,
        string environment = "Development",
        IReadOnlyDictionary<string, string?>? settings = null,
        Action<IServiceCollection>? configureServices = null)
    {
        _environment = environment;
        _configureServices = configureServices;
        var all = new Dictionary<string, string?>(settings ?? new Dictionary<string, string?>());
        if (connectionString is not null)
        {
            all["ConnectionStrings:Postgres"] = connectionString;
        }

        _settings = all;
    }

    public CapturingLoggerProvider Logs { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);
        foreach (var (key, value) in _settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureLogging(logging => logging.AddProvider(Logs));
        builder.ConfigureServices(services => _configureServices?.Invoke(services));
    }
}
