using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using PolvorApp.SharedKernel.Storage;

namespace PolvorApp.Api.Platform.Storage;

internal static class StorageServiceCollectionExtensions
{
    /// <summary>Registers the validated storage settings and the S3-compatible object storage (ADR-0005).</summary>
    public static IServiceCollection AddPlatformStorage(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IValidateOptions<StorageOptions>, StorageOptionsValidator>();
        var storage = services.AddOptions<StorageOptions>().Configure(o => StorageOptions.Bind(o, configuration));

        // Build-time OpenAPI generation runs Program without configuration (bootstrap-platform D5).
        if (!BuildTimeDocumentGeneration.IsActive)
        {
            storage.ValidateOnStart();

            // Only a real host sweeps; build-time OpenAPI generation has no storage settings.
            services.AddHostedService<StoredObjectSweeper>();
        }

        services.AddSingleton<S3ObjectStorage>();
        services.AddSingleton<IObjectStorage>(provider => provider.GetRequiredService<S3ObjectStorage>());
        services.AddSingleton(provider =>
        {
            var s3 = provider.GetRequiredService<S3ObjectStorage>();
            var maxWait = TimeSpan.FromSeconds(provider.GetRequiredService<IOptions<StorageOptions>>().Value.BootstrapMaxWaitSeconds);
            // Wall-clock: the storage starts in real time, even when the host runs on a test clock.
            return new StorageBootstrapper(
                s3.Client, s3.Bucket, TimeProvider.System, provider.GetRequiredService<ILogger<StorageBootstrapper>>(), maxWait: maxWait);
        });
        return services;
    }
}
