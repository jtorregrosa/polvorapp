using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PolvorApp.Notifications.Rules;
using PolvorApp.SharedKernel.Modules;
using PolvorApp.SharedKernel.Time;

namespace PolvorApp.Notifications.Scheduling;

/// <summary>
/// Sends the event emails and every due delivery every <c>Notifications:DispatchIntervalSeconds</c>
/// (design D5). A failed pass is logged by type and retried at the next tick; only stopping ends the loop.
/// </summary>
internal sealed partial class NotificationDispatcher(
    NotificationPump pump, IOptions<NotificationsOptions> options, TimeProvider time, ILogger<NotificationDispatcher> logger) : BackgroundService
{
    /// <summary>Longest a pass may take, so a large backlog never delays the next tick for long.</summary>
    public static readonly TimeSpan PassBudget = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            LogDisabled(logger);
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.DispatchIntervalSeconds), time);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await pump.PumpAsync(PassBudget, stoppingToken);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                LogFailed(logger, exception.GetType().Name);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Notifications are disabled (Notifications:Enabled=false): no notification email is sent")]
    private static partial void LogDisabled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "A notification dispatch pass failed ({ErrorType}); it runs again at the next tick")]
    private static partial void LogFailed(ILogger logger, string errorType);
}

/// <summary>
/// Works out the scheduled notifications at start-up and then every 15 minutes, from 08:00 Europe/Madrid
/// (design D5). Each run creates only what is due and not yet created, so the repeated runs of a day are
/// harmless.
/// </summary>
internal sealed partial class NotificationScheduler(
    IServiceScopeFactory scopes, IOptions<NotificationsOptions> options, TimeProvider time, ILogger<NotificationScheduler> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    /// <summary>Runs the scheduled notifications if it is 08:00 or later in Europe/Madrid; false when too early.</summary>
    public async Task<bool> RunIfDueAsync(CancellationToken cancellationToken)
    {
        // One reading of the clock for both the gate and the date.
        var local = FederationCalendar.Now(time);
        if (!Schedule.MaySendScheduled(local))
        {
            return false;
        }

        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ScheduledNotifications>().RunAsync(DateOnly.FromDateTime(local.DateTime), cancellationToken);
        return true;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(Interval, time);
        do
        {
            try
            {
                await RunIfDueAsync(stoppingToken);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                LogFailed(logger, exception.GetType().Name);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The scheduled notifications run failed ({ErrorType}); it runs again at the next tick")]
    private static partial void LogFailed(ILogger logger, string errorType);
}

/// <summary>
/// <c>send-notifications</c>: works out today's scheduled notifications at once, whatever the hour, then
/// sends what is due for up to two minutes (spec: Scheduled notifications). Exit code 1 when a step,
/// the event expansion or a delivery failed in this run (it still sends what it can), 0 otherwise.
/// </summary>
internal sealed partial class SendNotificationsCommand(
    IServiceScopeFactory scopes, NotificationPump pump, TimeProvider time, ILogger<SendNotificationsCommand> logger) : IHostCommand
{
    public static readonly TimeSpan Budget = TimeSpan.FromMinutes(2);

    public string Verb => "send-notifications";

    public async Task<int> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        ScheduledRunResult? scheduled = null;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            scheduled = await scope.ServiceProvider.GetRequiredService<ScheduledNotifications>().RunAsync(FederationCalendar.Today(time), cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogScheduleFailed(logger, exception.GetType().Name);
        }

        var pumped = await pump.PumpAsync(Budget, cancellationToken);
        var sent = pumped.Sent;
        LogDone(
            logger, scheduled?.Digests ?? 0, scheduled?.CloseReminders ?? 0, scheduled?.MilestoneReminders ?? 0,
            sent.Sent, sent.Skipped, sent.Retried + sent.Failed);
        var failed = scheduled is null || scheduled.FailedSteps.Count > 0 || pumped.ExpansionFailed || sent.Retried + sent.Failed > 0;
        return failed ? 1 : 0;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Notifications: {Digests} digests, {CloseReminders} close reminders and {MilestoneReminders} milestone reminders created; {Sent} sent, {Skipped} skipped, {Failed} failed")]
    private static partial void LogDone(ILogger logger, int digests, int closeReminders, int milestoneReminders, int sent, int skipped, int failed);

    [LoggerMessage(Level = LogLevel.Error, Message = "The scheduled notifications could not be worked out ({ErrorType}); what is due is sent anyway")]
    private static partial void LogScheduleFailed(ILogger logger, string errorType);
}
