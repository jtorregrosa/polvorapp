using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PolvorApp.SharedKernel.Modules;

namespace PolvorApp.AuditPrivacy.Retention;

/// <summary>
/// Runs the audit retention purge a minute after start-up and then once a day (design D3). A failed
/// run is logged by type and retried at the next tick; only stopping ends the loop.
/// </summary>
internal sealed partial class AuditRetentionService(
    IServiceScopeFactory scopes, IOptions<AuditRetentionOptions> options, TimeProvider time, ILogger<AuditRetentionService> logger) : BackgroundService
{
    public static readonly TimeSpan FirstDelay = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan Interval = TimeSpan.FromDays(1);

    /// <summary>Runs one purge in a scope of its own.</summary>
    public async Task<AuditPurgeResult> RunOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<AuditPurge>().RunAsync(cancellationToken);
        if (result.Total > 0)
        {
            LogPurged(logger, result.Security, result.Standard);
        }

        return result;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.PurgeEnabled)
        {
            LogDisabled(logger);
            return;
        }

        await Task.Delay(FirstDelay, time, stoppingToken);
        using var timer = new PeriodicTimer(Interval, time);
        do
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                LogFailed(logger, exception);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Audit retention purge deleted {Security} security and {Standard} other entries")]
    private static partial void LogPurged(ILogger logger, int security, int standard);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The audit retention purge is disabled (Audit:PurgeEnabled=false)")]
    private static partial void LogDisabled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "The audit retention purge failed; it runs again at the next tick")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}

/// <summary><c>purge-audit</c>: runs the audit retention purge once, for operators. Exit code 1 when it failed.</summary>
internal sealed partial class PurgeAuditCommand(AuditRetentionService service, ILogger<PurgeAuditCommand> logger) : IHostCommand
{
    public string Verb => "purge-audit";

    public async Task<int> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        try
        {
            var result = await service.RunOnceAsync(cancellationToken);
            LogDone(logger, result.Security, result.Standard);
            return 0;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogFailed(logger, exception);
            return 1;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "purge-audit: {Security} security and {Standard} other entries deleted")]
    private static partial void LogDone(ILogger logger, int security, int standard);

    [LoggerMessage(Level = LogLevel.Error, Message = "purge-audit failed")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
