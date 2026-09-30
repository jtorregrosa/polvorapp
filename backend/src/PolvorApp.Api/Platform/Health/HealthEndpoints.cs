using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace PolvorApp.Api.Platform.Health;

internal static class HealthEndpoints
{
    private const string ReadyTag = "ready";

    // Below the default Npgsql connect timeout, so probes never pile up.
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(3);

    public static IServiceCollection AddPlatformHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database", tags: [ReadyTag], timeout: CheckTimeout);
        return services;
    }

    /// <summary>Anonymous liveness and readiness endpoints (spec: Health endpoints).</summary>
    public static IEndpointRouteBuilder MapPlatformHealthChecks(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = WriteAsync,
        });
        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains(ReadyTag),
            ResponseWriter = WriteAsync,
        });
        return endpoints;
    }

    /// <summary>Writes names and statuses only: no descriptions, exceptions or connection data.</summary>
    private static Task WriteAsync(HttpContext context, HealthReport report)
    {
        var body = new HealthResponse(
            report.Status.ToString(),
            [.. report.Entries.Select(entry => new HealthCheckEntry(entry.Key, entry.Value.Status.ToString()))]);

        context.Response.ContentType = "application/json";
        return JsonSerializer.SerializeAsync(context.Response.Body, body, HealthJsonContext.Default.HealthResponse, context.RequestAborted);
    }
}

internal sealed record HealthResponse(string Status, IReadOnlyList<HealthCheckEntry> Checks);

internal sealed record HealthCheckEntry(string Name, string Status);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(HealthResponse))]
internal sealed partial class HealthJsonContext : JsonSerializerContext;
