using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.Notifications.Emails;
using PolvorApp.Notifications.Persistence;
using PolvorApp.Notifications.Rules;
using PolvorApp.SharedKernel.Email;

namespace PolvorApp.Notifications.Sending;

/// <summary>What one sending pass did.</summary>
internal sealed record SendResult(int Sent, int Skipped, int Retried, int Failed)
{
    public static readonly SendResult None = new(0, 0, 0, 0);

    public int Claimed => Sent + Skipped + Retried + Failed;

    public SendResult Add(SendResult other) => new(Sent + other.Sent, Skipped + other.Skipped, Retried + other.Retried, Failed + other.Failed);
}

/// <summary>
/// Sends the deliveries that are due (design D5, D6).
/// <list type="bullet">
/// <item>Due rows are claimed with a lease that is committed before any SMTP work, and each row's lease is
/// taken again (compare-and-set) just before it is sent, so a row is sent by one process only even when a
/// slow batch outlives its first lease.</item>
/// <item>Each row is re-checked first: a recipient who no longer wants it, or a fact that no longer holds,
/// makes it <c>SKIPPED</c> with the reason in <c>last_error</c>.</item>
/// <item>An SMTP failure is retried with growing intervals for a day; a delivery that cannot be prepared
/// (a bug, not an outage) fails at once.</item>
/// <item>Every state change is a conditional update on a <c>PENDING</c> row, never a tracked entity.</item>
/// </list>
/// Logs carry the delivery id, template and outcome, never the address, subject or body (NFR-12).
/// </summary>
internal sealed partial class DeliverySender(
    NotificationsDbContext db,
    IUserDirectory users,
    ICatalogDirectory catalog,
    DeliveryContents contents,
    NotificationEmails emails,
    IFederationSettings federation,
    IEmailSender sender,
    TimeProvider time,
    ILogger<DeliverySender> logger)
{
    /// <summary>Small enough that a batch sent at the SMTP timeout (30 s each) stays within one lease.</summary>
    public const int BatchSize = 10;

    public static readonly TimeSpan Lease = TimeSpan.FromMinutes(10);

    /// <summary>Claims and sends up to <see cref="BatchSize"/> due deliveries.</summary>
    public async Task<SendResult> SendDueAsync(CancellationToken cancellationToken)
    {
        // Read before claiming: a failure here leaves no row leased, and rendering never reads the settings
        // again (a database outage is retried, never marked failed).
        var recipients = await Recipients.LoadAsync(users, catalog, db, cancellationToken);
        var settings = await federation.GetAsync(cancellationToken);
        var claimed = await ClaimAsync(cancellationToken);
        var result = SendResult.None;
        var done = 0;
        try
        {
            foreach (var id in claimed)
            {
                result = result.Add(await SendOneAsync(id, recipients, settings, cancellationToken));
                done++;
            }
        }
        finally
        {
            if (done < claimed.Count)
            {
                await ReleaseAsync(claimed.Skip(done).ToList());
            }
        }

        return result;
    }

    private async Task<List<Guid>> ClaimAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        await using var transaction = await db.BeginWriteAsync(cancellationToken);
        var ids = await db.Database.SqlQuery<Guid>(
                $"SELECT id AS \"Value\" FROM notifications.notification_deliveries WHERE status = 'PENDING' AND next_attempt_at <= {now} ORDER BY next_attempt_at, id LIMIT {BatchSize} FOR UPDATE SKIP LOCKED")
            .ToListAsync(cancellationToken);
        if (ids.Count > 0)
        {
            var leasedUntil = now + Lease;
            await db.Deliveries.Where(d => ids.Contains(d.Id))
                .ExecuteUpdateAsync(d => d.SetProperty(x => x.NextAttemptAt, leasedUntil), cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return ids;
    }

    private async Task<SendResult> SendOneAsync(Guid id, Recipients recipients, FederationSettingsSnapshot settings, CancellationToken cancellationToken)
    {
        var delivery = await db.Deliveries.AsNoTracking().SingleOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (delivery is not { Status: DeliveryStatus.Pending } || !await RenewLeaseAsync(delivery, cancellationToken))
        {
            // Deleted, finished or taken by another process since it was claimed.
            return SendResult.None;
        }

        EmailMessage email;
        try
        {
            var recipient = recipients.Find(delivery.UserId);
            var prepared = recipient is null ? PreparedContent.Skip("UserGone")
                : !recipients.Wants(delivery.UserId, delivery.Kind) ? PreparedContent.Skip("NotWanted")
                : await contents.BuildAsync(delivery, recipient, recipients.ComparsasOf(delivery.UserId), cancellationToken);
            if (recipient is null || prepared.Content is null)
            {
                var reason = prepared.SkipReason ?? "NoContent";
                await FinishAsync(delivery.Id, DeliveryStatus.Skipped, delivery.Attempts, reason, sentAt: null, cancellationToken);
                LogSkipped(logger, delivery.Id, delivery.Template, reason);
                return new SendResult(0, 1, 0, 0);
            }

            email = emails.Render(recipient, prepared.Content, settings);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Data a template needs is missing, or a text cannot be rendered: retrying will not help.
            var error = exception.GetType().Name;
            await FinishAsync(delivery.Id, DeliveryStatus.Failed, delivery.Attempts, error, sentAt: null, cancellationToken);
            LogUnprepared(logger, delivery.Id, delivery.Template, error);
            return new SendResult(0, 0, 0, 1);
        }

        var attempts = delivery.Attempts + 1;
        try
        {
            await sender.SendAsync(email, cancellationToken);
        }
        catch (EmailDeliveryException exception)
        {
            return await RetryOrFailAsync(delivery, attempts, Describe(exception), cancellationToken);
        }

        try
        {
            await FinishAsync(delivery.Id, DeliveryStatus.Sent, attempts, lastError: null, time.GetUtcNow(), cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // The SMTP server has it: never retry from here. The row stays leased and is sent again after
            // the lease only if this record is never written (spec: the one duplicate the design accepts).
            LogSentNotRecorded(logger, delivery.Id, delivery.Template, exception.GetType().Name);
        }

        return new SendResult(1, 0, 0, 0);
    }

    private async Task<SendResult> RetryOrFailAsync(NotificationDelivery delivery, int attempts, string error, CancellationToken cancellationToken)
    {
        if (RetrySchedule.Next(attempts, delivery.CreatedAt, time.GetUtcNow()) is { } next)
        {
            await db.Deliveries.Where(d => d.Id == delivery.Id && d.Status == DeliveryStatus.Pending)
                .ExecuteUpdateAsync(
                    d => d.SetProperty(x => x.Attempts, attempts).SetProperty(x => x.NextAttemptAt, next).SetProperty(x => x.LastError, error),
                    cancellationToken);
            LogRetry(logger, delivery.Id, delivery.Template, attempts, error, next);
            return new SendResult(0, 0, 1, 0);
        }

        await FinishAsync(delivery.Id, DeliveryStatus.Failed, attempts, error, sentAt: null, cancellationToken);
        LogGaveUp(logger, delivery.Id, delivery.Template, attempts, error);
        return new SendResult(0, 0, 0, 1);
    }

    /// <summary>Takes the row's lease again, only if it is still pending and still under the lease this process took.</summary>
    private async Task<bool> RenewLeaseAsync(NotificationDelivery delivery, CancellationToken cancellationToken)
    {
        var leasedUntil = time.GetUtcNow() + Lease;
        return await db.Deliveries
            .Where(d => d.Id == delivery.Id && d.Status == DeliveryStatus.Pending && d.NextAttemptAt == delivery.NextAttemptAt)
            .ExecuteUpdateAsync(d => d.SetProperty(x => x.NextAttemptAt, leasedUntil), cancellationToken) == 1;
    }

    private Task<int> FinishAsync(Guid id, DeliveryStatus status, int attempts, string? lastError, DateTimeOffset? sentAt, CancellationToken cancellationToken) =>
        db.Deliveries.Where(d => d.Id == id && d.Status == DeliveryStatus.Pending)
            .ExecuteUpdateAsync(
                d => d.SetProperty(x => x.Status, status).SetProperty(x => x.Attempts, attempts)
                    .SetProperty(x => x.LastError, lastError).SetProperty(x => x.SentAt, sentAt),
                cancellationToken);

    /// <summary>Gives back the rows a failed batch did not reach, so they are tried at the next pass instead of after the lease.</summary>
    private async Task ReleaseAsync(List<Guid> ids)
    {
        try
        {
            var now = time.GetUtcNow();
            await db.Deliveries.Where(d => ids.Contains(d.Id) && d.Status == DeliveryStatus.Pending)
                .ExecuteUpdateAsync(d => d.SetProperty(x => x.NextAttemptAt, now), CancellationToken.None);
        }
        catch (Exception exception)
        {
            // The leases expire on their own; the batch's own failure is what the caller reports.
            LogReleaseFailed(logger, ids.Count, exception.GetType().Name);
        }
    }

    /// <summary>The SMTP phase and PII-free code, e.g. <c>Send 550 MailboxUnavailable</c>.</summary>
    private static string Describe(EmailDeliveryException exception)
    {
        var text = exception.Phase is null ? "SMTP delivery failed" : $"{exception.Phase} {exception.Code}".Trim();
        return text.Length <= NotificationDelivery.LastErrorMaxLength ? text : text[..NotificationDelivery.LastErrorMaxLength];
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Notification {DeliveryId} ({Template}) skipped: {Reason}")]
    private static partial void LogSkipped(ILogger logger, Guid deliveryId, string template, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Notification {DeliveryId} ({Template}) could not be prepared ({ErrorType}); it will not be retried")]
    private static partial void LogUnprepared(ILogger logger, Guid deliveryId, string template, string errorType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Notification {DeliveryId} ({Template}) failed on attempt {Attempts} ({Error}); retrying at {NextAttemptAt}")]
    private static partial void LogRetry(ILogger logger, Guid deliveryId, string template, int attempts, string error, DateTimeOffset nextAttemptAt);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Notification {DeliveryId} ({Template}) given up after {Attempts} attempts: {Error}")]
    private static partial void LogGaveUp(ILogger logger, Guid deliveryId, string template, int attempts, string error);

    [LoggerMessage(Level = LogLevel.Error, Message = "Notification {DeliveryId} ({Template}) was sent but could not be recorded ({ErrorType}); it may be sent again after its lease")]
    private static partial void LogSentNotRecorded(ILogger logger, Guid deliveryId, string template, string errorType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Count} leased notifications could not be released ({ErrorType}); they are retried after their lease")]
    private static partial void LogReleaseFailed(ILogger logger, int count, string errorType);
}
