using PolvorApp.Api.Platform.Storage;
using PolvorApp.SharedKernel.Persistence;

namespace PolvorApp.Api.Platform.Database;

/// <summary>
/// The <c>migrate</c> host command: applies every module's migrations in order, then makes sure the
/// object storage bucket exists (design D1 of add-arquebusier-photos). Migrations never
/// run on web startup (spec: Local environment with one command); compose runs this command in a
/// one-shot service before the API starts.
/// </summary>
internal static partial class MigrateCommand
{
    public const string Verb = "migrate";
    private const string StorageContext = "object storage";

    public static async Task<int> RunAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(MigrateCommand).FullName!);

        var current = "(startup)";
        try
        {
            await using var scope = services.CreateAsyncScope();
            var migrators = scope.ServiceProvider.GetServices<IDatabaseMigrator>().OrderBy(m => m.Order).ToList();
            if (migrators.Count == 0)
            {
                // A registration regression must not let the API start against an empty database.
                LogNoMigrators(logger);
                return 1;
            }

            foreach (var migrator in migrators)
            {
                current = migrator.Name;
                LogMigrating(logger, migrator.Name);
                await migrator.MigrateAsync(scope.ServiceProvider, cancellationToken);
            }

            LogCompleted(logger, migrators.Count);

            // Files live next to the database: the bucket must exist before the API starts (ADR-0005).
            current = StorageContext;
            await scope.ServiceProvider.GetRequiredService<StorageBootstrapper>().EnsureBucketAsync(cancellationToken);
            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            LogCancelled(logger, current);
            return 1;
        }
        catch (Exception exception)
        {
            // Covers configuration errors, unreachable databases and failing migrations.
            LogFailed(logger, current, exception);
            return 1;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Applying migrations of {Context}")]
    private static partial void LogMigrating(ILogger logger, string context);

    [LoggerMessage(Level = LogLevel.Information, Message = "Migrations applied for {Count} module contexts")]
    private static partial void LogCompleted(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Migration was cancelled in {Context}")]
    private static partial void LogCancelled(ILogger logger, string context);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Migration failed in {Context}")]
    private static partial void LogFailed(ILogger logger, string context, Exception exception);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Migration failed: no module registers a database migrator")]
    private static partial void LogNoMigrators(ILogger logger);
}
