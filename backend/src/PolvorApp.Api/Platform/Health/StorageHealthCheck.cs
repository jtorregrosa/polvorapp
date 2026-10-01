using Amazon.S3.Model;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using PolvorApp.Api.Platform.Storage;

namespace PolvorApp.Api.Platform.Health;

/// <summary>Readiness: the object storage answers and the configured bucket exists (ADR-0005).</summary>
internal sealed class StorageHealthCheck(S3ObjectStorage storage) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await storage.Client.HeadBucketAsync(new HeadBucketRequest { BucketName = storage.Bucket }, cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // The exception is kept for the server log only; the HTTP writer never exposes it.
            return new HealthCheckResult(context.Registration.FailureStatus, exception: exception);
        }
    }
}
