using Microsoft.Extensions.Options;
using Npgsql;

namespace PolvorApp.Api.Platform.Database;

internal static class DatabaseServiceCollectionExtensions
{
    /// <summary>Registers validated database options and a shared <see cref="NpgsqlDataSource"/>.</summary>
    public static IServiceCollection AddDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<DatabaseOptions>, DatabaseOptionsValidator>();
        var options = services.AddOptions<DatabaseOptions>()
            .Configure(o => o.ConnectionString = configuration[DatabaseOptions.ConnectionStringKey]);

        // Build-time OpenAPI generation runs Program without configuration (design D5).
        if (!BuildTimeDocumentGeneration.IsActive)
        {
            options.ValidateOnStart();
        }

        services.AddSingleton(sp =>
            NpgsqlDataSource.Create(sp.GetRequiredService<IOptions<DatabaseOptions>>().Value.ConnectionString!));

        return services;
    }
}
