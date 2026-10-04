using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.FestivalEditions.Persistence;
using PolvorApp.Notifications.Contracts;
using PolvorApp.Notifications.Persistence;
using PolvorApp.Notifications.Scheduling;
using PolvorApp.Notifications.Sending;
using PolvorApp.SharedKernel.Email;

namespace PolvorApp.Api.Tests.Infrastructure;

/// <summary>Notification helpers: run the dispatcher's steps directly and read the module's tables.</summary>
internal static class NotificationData
{
    /// <summary>Turns every kind off for <paramref name="userIds"/>, e.g. the shared users of a test host, so they get no email.</summary>
    public static async Task OptOutOfEverythingAsync(this IServiceProvider services, params Guid[] userIds)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        db.OptOuts.AddRange(userIds.SelectMany(user => Enum.GetValues<NotificationKind>()
            .Select(kind => new NotificationOptOut { UserId = user, Kind = kind, CreatedAt = DateTimeOffset.UtcNow })));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Sets the planned close date of an edition directly (required once it is in progress).</summary>
    public static async Task SetOrdersCloseOnAsync(this IServiceProvider services, Guid editionId, DateOnly? closeOn)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<FestivalEditionsDbContext>().Editions.Where(e => e.Id == editionId)
            .ExecuteUpdateAsync(e => e.SetProperty(x => x.OrdersCloseOn, closeOn), TestContext.Current.CancellationToken);
    }

    public static async Task<int> ExpandAsync(this IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<EventExpander>().ExpandAsync(TestContext.Current.CancellationToken);
    }

    public static async Task<SendResult> SendDueAsync(this IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<DeliverySender>().SendDueAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Expands the events and sends what is due, as one dispatcher pass does.</summary>
    public static async Task<SendResult> PumpAsync(this IServiceProvider services) =>
        (await services.GetRequiredService<NotificationPump>().PumpAsync(TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken)).Sent;

    public static async Task<ScheduledRunResult> RunScheduledAsync(this IServiceProvider services, DateOnly today)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ScheduledNotifications>().RunAsync(today, TestContext.Current.CancellationToken);
    }

    public static async Task<List<NotificationDelivery>> DeliveriesAsync(this IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<NotificationsDbContext>().Deliveries.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken);
    }
}

/// <summary>An <see cref="IEmailSender"/> that keeps every message and fails the next <see cref="FailNext"/> sends.</summary>
public sealed class RecordingEmailSender : IEmailSender
{
    private int _failNext;

    public ConcurrentQueue<EmailMessage> Sent { get; } = new();

    /// <summary>How many of the next sends fail as an unreachable SMTP server would.</summary>
    public int FailNext
    {
        get => Volatile.Read(ref _failNext);
        set => Volatile.Write(ref _failNext, value);
    }

    public IReadOnlyList<EmailMessage> To(string address) => Sent.Where(m => m.ToAddress == address).ToList();

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        if (Interlocked.Decrement(ref _failNext) >= 0)
        {
            throw new EmailDeliveryException();
        }

        Interlocked.Exchange(ref _failNext, 0);
        Sent.Enqueue(message);
        return Task.CompletedTask;
    }

    /// <summary>Registers this sender in place of SMTP.</summary>
    public void Register(IServiceCollection services) => services.AddSingleton<IEmailSender>(this);
}
