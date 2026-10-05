using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.FestivalEditions.Contracts;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.Notifications.Contracts;
using PolvorApp.Notifications.Emails;
using PolvorApp.Notifications.Persistence;
using PolvorApp.Notifications.Rules;
using PolvorApp.Notifications.Sending;

namespace PolvorApp.Notifications.Scheduling;

/// <summary>What one scheduled run created, and the steps that failed (logged, retried at the next run).</summary>
internal sealed record ScheduledRunResult(int Digests, int CloseReminders, int MilestoneReminders, IReadOnlyList<string> FailedSteps);

/// <summary>
/// The daily work (design D5, D8–D10): the month's license digest, the planned close reminders and the
/// milestone reminders due on <c>today</c>, plus the retention clean-up. Each step runs in its own
/// transaction under its own advisory try-lock, so a failing step never stops the others and two
/// processes never do the same step at once. Deliveries are inserted with <c>ON CONFLICT DO NOTHING</c>
/// and the digest is recorded per month, so running again the same day, or after a restart, creates
/// nothing twice.
/// </summary>
internal sealed partial class ScheduledNotifications(
    NotificationsDbContext db,
    IUserDirectory users,
    ICatalogDirectory catalog,
    IArquebusierFacts registry,
    IEditionDirectory editions,
    IEditionEntries entries,
    IFederationSettings settings,
    TimeProvider time,
    ILogger<ScheduledNotifications> logger)
{
    /// <summary>Delivery records are kept one year (spec: Notification delivery).</summary>
    public static readonly TimeSpan DeliveryRetention = TimeSpan.FromDays(365);

    /// <summary>Processed events are kept a month, enough to investigate a complaint.</summary>
    public static readonly TimeSpan EventRetention = TimeSpan.FromDays(30);

    public async Task<ScheduledRunResult> RunAsync(DateOnly today, CancellationToken cancellationToken)
    {
        var recipients = await Recipients.LoadAsync(users, catalog, db, cancellationToken);
        var now = time.GetUtcNow();
        var failed = new List<string>();
        var digests = await StepAsync(1, "LicenseDigest", () => DigestsAsync(today, recipients, now, cancellationToken), failed, cancellationToken);
        var close = await StepAsync(2, "CloseReminders", () => CloseRemindersAsync(today, recipients, now, cancellationToken), failed, cancellationToken);
        var milestones = await StepAsync(3, "MilestoneReminders", () => MilestoneRemindersAsync(today, recipients, now, cancellationToken), failed, cancellationToken);
        await StepAsync(4, "Retention", () => CleanUpAsync(now, cancellationToken), failed, cancellationToken);
        return new ScheduledRunResult(digests, close, milestones, failed);
    }

    private async Task<int> StepAsync(int key, string name, Func<Task<int>> work, List<string> failed, CancellationToken cancellationToken)
    {
        try
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.BeginWriteAsync(cancellationToken);
            if (!await db.TryLockScheduledStepAsync(key, cancellationToken))
            {
                LogBusy(logger, name);
                return 0;
            }

            var created = await work();
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return created;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            failed.Add(name);
            LogStepFailed(logger, name, exception.GetType().Name);
            return 0;
        }
    }

    private async Task<int> CleanUpAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var deliveries = await db.Deliveries.Where(d => d.CreatedAt < now - DeliveryRetention).ExecuteDeleteAsync(cancellationToken);
        var events = await db.Events.Where(e => e.ProcessedAt != null && e.ProcessedAt < now - EventRetention).ExecuteDeleteAsync(cancellationToken);
        return deliveries + events;
    }

    /// <summary>Once per month, on its first run: one digest per FiringChief with something to report (spec: License digest).</summary>
    private async Task<int> DigestsAsync(DateOnly today, Recipients recipients, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var period = today.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        if (await db.Runs.AnyAsync(r => r.Job == NotificationRun.LicenseDigestJob && r.Period == period, cancellationToken))
        {
            return 0;
        }

        db.Runs.Add(new NotificationRun { Job = NotificationRun.LicenseDigestJob, Period = period, RanAt = now });
        var chiefs = recipients.FiringChiefsWithComparsas(NotificationKind.LicenseDigest).ToList();
        var comparsaIds = chiefs.SelectMany(c => recipients.ComparsasOf(c.Id)).Distinct().ToList();
        var counts = LicenseDigestRule.Count(await registry.ListAsync(comparsaIds, cancellationToken), today).ToDictionary(c => c.ComparsaId);
        var created = 0;
        foreach (var chief in chiefs)
        {
            var digest = recipients.ComparsasOf(chief.Id).Where(counts.ContainsKey).Order().Select(id => counts[id])
                .Select(c => new DigestEntry(c.ComparsaId, c.Missing, c.Pending, c.Expired, c.ExpiringSoon))
                .ToList();
            if (digest.Count > 0 && await AddAsync(
                    chief.Id, NotificationKind.LicenseDigest, NotificationTemplates.LicenseDigest, Topics.Digest(today),
                    new DeliveryData { Date = today, Digest = digest }, now, cancellationToken))
            {
                created++;
            }
        }

        return created;
    }

    /// <summary>While the orders are open, to the FiringChiefs of each comparsa whose order is not submitted (spec: Planned close reminders).</summary>
    private async Task<int> CloseRemindersAsync(DateOnly today, Recipients recipients, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (await editions.GetCurrentAsync(cancellationToken) is not { OrdersOpen: true, OrdersCloseOn: { } closeOn } edition
            || Schedule.CloseReminderFor(today, closeOn, (await settings.GetAsync(cancellationToken)).CloseReminderLeadDays) is not { } reminder)
        {
            return 0;
        }

        var orders = (await entries.ListOrdersAsync(edition.Id, cancellationToken)).ToDictionary(o => o.ComparsaId);
        var template = reminder == CloseReminder.Week ? NotificationTemplates.OrdersClosingSoon : NotificationTemplates.OrdersClosingTomorrow;
        var created = 0;
        foreach (var comparsaId in recipients.AssignedComparsas())
        {
            var order = orders.GetValueOrDefault(comparsaId);
            PendingOrderState? state = order?.Status switch
            {
                null => PendingOrderState.NotPrepared,
                OrderStatus.Draft => PendingOrderState.Draft,
                OrderStatus.Returned => PendingOrderState.Returned,
                _ => null,
            };
            if (state is null)
            {
                continue;
            }

            var data = new DeliveryData { EditionId = edition.Id, ComparsaId = comparsaId, OrderId = order?.OrderId, Date = closeOn, State = state };
            foreach (var chief in recipients.FiringChiefsOf(comparsaId, NotificationKind.OrderWindow))
            {
                if (await AddAsync(chief.Id, NotificationKind.OrderWindow, template, Topics.Close(reminder, edition.Id, comparsaId, closeOn), data, now, cancellationToken))
                {
                    created++;
                }
            }
        }

        return created;
    }

    /// <summary>Milestones with <c>notify</c> from 0 days up to the settings' lead time ahead (spec: Milestone reminders).</summary>
    private async Task<int> MilestoneRemindersAsync(DateOnly today, Recipients recipients, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var created = 0;
        var (from, to) = Schedule.MilestoneWindow(today, (await settings.GetAsync(cancellationToken)).MilestoneLeadDays);
        foreach (var milestone in await editions.ListMilestonesToNotifyAsync(from, to, cancellationToken))
        {
            var audience = recipients.WithRole(UserRole.Admin, NotificationKind.MilestoneReminder);
            if (Schedule.RemindsFiringChiefs(milestone.EditionStatus))
            {
                audience = audience.Concat(recipients.WithRole(UserRole.FiringChief, NotificationKind.MilestoneReminder));
            }

            var data = new DeliveryData { EditionId = milestone.EditionId, MilestoneId = milestone.Id, Date = milestone.Date };
            foreach (var user in audience)
            {
                if (await AddAsync(
                        user.Id, NotificationKind.MilestoneReminder, NotificationTemplates.MilestoneReminder, Topics.Milestone(milestone.Id, milestone.Date),
                        data, now, cancellationToken))
                {
                    created++;
                }
            }
        }

        return created;
    }

    private Task<bool> AddAsync(
        Guid userId, NotificationKind kind, string template, string topic, DeliveryData data, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.TryAddAsync(
            new NotificationDelivery
            {
                Id = Guid.CreateVersion7(now),
                UserId = userId,
                Kind = kind,
                Template = template,
                Topic = topic,
                Data = data.Serialize(),
                NextAttemptAt = now,
                CreatedAt = now,
            },
            cancellationToken);

    [LoggerMessage(Level = LogLevel.Information, Message = "Scheduled notification step {Step} skipped: another process is running it")]
    private static partial void LogBusy(ILogger logger, string step);

    [LoggerMessage(Level = LogLevel.Error, Message = "Scheduled notification step {Step} failed ({ErrorType}); it runs again at the next run")]
    private static partial void LogStepFailed(ILogger logger, string step, string errorType);
}
