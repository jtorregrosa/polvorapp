using PolvorApp.SharedKernel.Images;

namespace PolvorApp.Api.Platform.Images;

internal static class ImageServiceCollectionExtensions
{
    /// <summary>Registers the image normaliser every module uses before storing an image (ADR-0005, SEC-12).</summary>
    public static IServiceCollection AddPlatformImages(this IServiceCollection services)
    {
        services.AddSingleton<SkiaImageNormalizer>();
        services.AddSingleton<IImageNormalizer>(provider => provider.GetRequiredService<SkiaImageNormalizer>());
        return services;
    }
}
