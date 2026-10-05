using PolvorApp.SharedKernel.Images;
using PolvorApp.SharedKernel.Seeding;

namespace PolvorApp.Api.Platform.Images;

internal static class ImageServiceCollectionExtensions
{
    /// <summary>
    /// Registers the image normaliser every module uses before storing an image (ADR-0005, SEC-12),
    /// and what the seeders use: the committed seed images and the specimen license-card painter
    /// (realistic-seed-data, designs D5 and D6). Nothing is loaded until a seeder asks.
    /// </summary>
    public static IServiceCollection AddPlatformImages(this IServiceCollection services)
    {
        services.AddSingleton<SkiaImageNormalizer>();
        services.AddSingleton<IImageNormalizer>(provider => provider.GetRequiredService<SkiaImageNormalizer>());
        services.AddSingleton(_ => SyntheticImages.FromBuildOutput());
        services.AddSingleton<ISpecimenCardPainter, SkiaSpecimenCardPainter>();
        return services;
    }
}
