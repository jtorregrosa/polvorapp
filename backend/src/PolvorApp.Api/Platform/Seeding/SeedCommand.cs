using PolvorApp.SharedKernel.Seeding;

namespace PolvorApp.Api.Platform.Seeding;

/// <summary>
/// The <c>seed</c> host command: runs every registered <see cref="IDataSeeder"/> in order, only in
/// environments that hold synthetic data (spec: Guarded synthetic seed, SEC-11). Seeding is not
/// transactional: a failed run leaves partial synthetic data; reset the database and rerun.
/// </summary>
internal static partial class SeedCommand
{
    public const string Verb = "seed";

    /// <summary>Environments that only ever contain synthetic data (NFR-13). Everything else is refused.</summary>
    public static readonly IReadOnlySet<string> AllowedEnvironments =
        new HashSet<string>(["Development", "Staging", "Testing"], StringComparer.OrdinalIgnoreCase);

    public static async Task<int> RunAsync(IServiceProvider services, IHostEnvironment environment, CancellationToken cancellationToken)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(SeedCommand).FullName!);

        if (!AllowedEnvironments.Contains(environment.EnvironmentName))
        {
            LogRefused(logger, environment.EnvironmentName);
            return 1;
        }

        SeedDataset dataset;
        try
        {
            // Checked before any seeder runs, so an unknown name writes nothing. The host always
            // registers IConfiguration; a missing one is a wiring error, not "use the default".
            dataset = SeedDatasets.Read(services.GetRequiredService<IConfiguration>());
        }
        catch (InvalidOperationException exception)
        {
            LogInvalidDataset(logger, exception);
            return 1;
        }

        LogDataset(logger, dataset);
        try
        {
            await using var scope = services.CreateAsyncScope();
            var seeders = scope.ServiceProvider.GetServices<IDataSeeder>().OrderBy(s => s.Order).ToList();
            foreach (var seeder in seeders)
            {
                LogSeederStarting(logger, seeder.GetType().Name);
                await seeder.SeedAsync(cancellationToken);
            }

            LogCompleted(logger, seeders.Count, environment.EnvironmentName);
            return 0;
        }
        catch (OperationCanceledException)
        {
            LogCancelled(logger);
            return 1;
        }
        catch (Exception exception)
        {
            // Covers configuration errors raised while resolving seeders and failures while seeding.
            LogFailed(logger, exception);
            return 1;
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Seeding refused in environment '{Environment}': seeding only runs in Development, Staging or Testing (SEC-11).")]
    private static partial void LogRefused(ILogger logger, string environment);

    [LoggerMessage(Level = LogLevel.Error, Message = "Seeding refused: the dataset setting could not be read")]
    private static partial void LogInvalidDataset(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeding the {Dataset} dataset")]
    private static partial void LogDataset(ILogger logger, SeedDataset dataset);

    [LoggerMessage(Level = LogLevel.Information, Message = "Running seeder {Seeder}")]
    private static partial void LogSeederStarting(ILogger logger, string seeder);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Seeding was cancelled; the database may contain partial synthetic data")]
    private static partial void LogCancelled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Seeding failed; the database may contain partial synthetic data")]
    private static partial void LogFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeding completed: {Count} seeders in {Environment}")]
    private static partial void LogCompleted(ILogger logger, int count, string environment);
}
