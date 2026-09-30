using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace PolvorApp.Api.Platform.Health;

/// <summary>Readiness: the database accepts connections and answers a trivial query.</summary>
internal sealed class DatabaseHealthCheck(NpgsqlDataSource dataSource) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var command = dataSource.CreateCommand("SELECT 1");
            await command.ExecuteScalarAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // The exception is kept for the server log only; the HTTP writer never exposes it.
            return new HealthCheckResult(context.Registration.FailureStatus, exception: exception);
        }
    }
}
