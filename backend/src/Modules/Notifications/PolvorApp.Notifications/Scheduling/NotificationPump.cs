using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PolvorApp.Notifications.Sending;

namespace PolvorApp.Notifications.Scheduling;

/// <summary>What one dispatch pass did; <see cref="ExpansionFailed"/> when the events could not be expanded.</summary>
internal sealed record PumpResult(SendResult Sent, bool ExpansionFailed);

/// <summary>
/// One dispatch pass (design D5): expands the waiting events, then sends the due deliveries, batch after
/// batch, until nothing is left or the pass has taken its budget. Each batch runs in its own scope. A
/// failing expansion is logged and never stops the sending: retries, digests and reminders keep going.
/// </summary>
internal sealed partial class NotificationPump(IServiceScopeFactory scopes, TimeProvider time, ILogger<NotificationPump> logger)
{
    public async Task<PumpResult> PumpAsync(TimeSpan budget, CancellationToken cancellationToken)
    {
        var deadline = time.GetUtcNow() + budget;
        var expansionFailed = false;
        try
        {
            while (time.GetUtcNow() < deadline)
            {
                await using var scope = scopes.CreateAsyncScope();
                if (await scope.ServiceProvider.GetRequiredService<EventExpander>().ExpandAsync(cancellationToken) == 0)
                {
                    break;
                }
            }
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            expansionFailed = true;
            LogExpansionFailed(logger, exception.GetType().Name);
        }

        var result = SendResult.None;
        while (time.GetUtcNow() < deadline)
        {
            await using var scope = scopes.CreateAsyncScope();
            var batch = await scope.ServiceProvider.GetRequiredService<DeliverySender>().SendDueAsync(cancellationToken);
            result = result.Add(batch);
            if (batch.Claimed == 0)
            {
                break;
            }
        }

        return new PumpResult(result, expansionFailed);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Notification events could not be expanded ({ErrorType}); the due deliveries are sent anyway")]
    private static partial void LogExpansionFailed(ILogger logger, string errorType);
}
