using PolvorApp.Api.Platform.Database;
using PolvorApp.Api.Platform.Seeding;
using PolvorApp.SharedKernel.Modules;

namespace PolvorApp.Api.Platform;

/// <summary>
/// One dispatcher for command-line verbs (<c>migrate</c>, <c>seed</c> and the verbs modules
/// contribute, e.g. <c>create-admin</c>). Without a verb (or with options only) the web server
/// starts; an unknown verb exits with a usage message instead of silently serving.
/// </summary>
internal static partial class HostCommands
{
    public const int Success = 0;
    public const int Failure = 1;
    public const int Usage = 2;
    public const int Cancelled = 130;

    /// <summary>The exit code of the command in <paramref name="args"/>, or null to start the web server.</summary>
    public static async Task<int?> TryRunAsync(WebApplication app, string[] args)
    {
        if (args is not [var verb, ..] || verb.StartsWith('-'))
        {
            return null;
        }

        await using var commandApp = app;
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(HostCommands).FullName!);
        var modules = app.Services.GetServices<IHostCommand>().ToList();
        var known = new[] { MigrateCommand.Verb, SeedCommand.Verb }.Concat(modules.Select(c => c.Verb)).ToList();
        if (known.Count != known.Distinct(StringComparer.Ordinal).Count())
        {
            throw new InvalidOperationException("Two host commands share a verb.");
        }

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        try
        {
            return verb switch
            {
                MigrateCommand.Verb => await MigrateCommand.RunAsync(app.Services, cancellation.Token),
                SeedCommand.Verb => await SeedCommand.RunAsync(app.Services, app.Environment, cancellation.Token),
                _ when modules.Find(c => c.Verb == verb) is { } command => await command.RunAsync(args[1..], cancellation.Token),
                _ => UnknownVerb(logger, known),
            };
        }
        catch (OperationCanceledException)
        {
            LogCancelled(logger, verb);
            return Cancelled;
        }
        catch (Exception exception)
        {
            // Type only: messages of configuration or database errors may carry values.
            var errorType = exception.GetType().Name;
            LogFailed(logger, verb, errorType);
            return Failure;
        }
    }

    private static int UnknownVerb(ILogger logger, IEnumerable<string> known)
    {
        LogUnknown(logger, string.Join(", ", known));
        return Usage;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unknown command. Available commands: {Commands}")]
    private static partial void LogUnknown(ILogger logger, string commands);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Command {Verb} was cancelled")]
    private static partial void LogCancelled(ILogger logger, string verb);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Command {Verb} failed ({ErrorType})")]
    private static partial void LogFailed(ILogger logger, string verb, string errorType);
}
