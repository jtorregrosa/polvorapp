using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.FestivalEditions.Editions;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.Notifications;
using PolvorApp.Notifications.Scheduling;
using PolvorApp.SharedKernel.Modules;
using static PolvorApp.Api.Tests.Infrastructure.OrderData;

namespace PolvorApp.Api.Tests.Notifications;

/// <summary>
/// The hosted services, the <c>send-notifications</c> command and a real delivery through SMTP (spec:
/// Scheduled notifications, Email content; design D5).
/// </summary>
public sealed class NotificationServicesTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private OrderTestHost _orders = null!;
    private SyntheticUser _chief = null!;

    public async ValueTask InitializeAsync()
    {
        _orders = await OrderTestHost.StartAsync(postgres, mailpit);
        await _orders.Services.OptOutOfEverythingAsync(_orders.Registry.AdminId, _orders.Registry.FiringChiefId);
        _chief = await _orders.Host.CreateUserAsync("jefe.servicios.avisos@example.test", locale: "ca-ES-valencia");
        await _orders.Host.AssignAsync(_orders.Own.Id, _chief.Id);
    }

    public async ValueTask DisposeAsync() => await _orders.DisposeAsync();

    [Fact]
    public void Both_hosted_services_are_registered_and_off_in_test_hosts()
    {
        var hosted = _orders.Services.GetServices<IHostedService>().ToList();

        Assert.Contains(hosted, s => s is NotificationDispatcher);
        Assert.Contains(hosted, s => s is NotificationScheduler);
        Assert.False(_orders.Services.GetRequiredService<IOptions<NotificationsOptions>>().Value.Enabled);
    }

    [Fact]
    public async Task The_command_sends_todays_reminders_whatever_the_hour_through_smtp()
    {
        var today = PolvorApp.SharedKernel.Time.FederationCalendar.Today(_orders.Host.Time);
        await _orders.Services.SaveEditionsAsync(new CalendarMilestone
        {
            Id = Guid.CreateVersion7(),
            EditionId = _orders.Current.Id,
            Date = today.AddDays(3),
            Title = "Curs sintètic",
            Notify = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        var command = _orders.Services.GetServices<IHostCommand>().Single(c => c.Verb == "send-notifications");

        var exitCode = await command.RunAsync([], TestContext.Current.CancellationToken);
        var again = await command.RunAsync([], TestContext.Current.CancellationToken);

        Assert.Equal((0, 0), (exitCode, again));
        var email = await mailpit.WaitForMessageAsync(_chief.Email);
        Assert.StartsWith("Recordatori: Curs sintètic", email.Subject, StringComparison.Ordinal);
        Assert.Contains($"/editions/{_orders.Current.Id}", email.Text, StringComparison.Ordinal);
        Assert.Equal(1, await mailpit.CountMessagesAsync(_chief.Email));
    }

    [Fact]
    public async Task The_scheduler_waits_until_eight_in_madrid()
    {
        var scheduler = _orders.Services.GetRequiredService<NotificationScheduler>();
        var local = PolvorApp.SharedKernel.Time.FederationCalendar.Now(_orders.Host.Time);
        var nextTwoAm = new DateTimeOffset(local.Date.AddDays(1).AddHours(2), local.Offset);
        _orders.Host.Time.SetUtcNow(nextTwoAm.ToUniversalTime());

        var early = await scheduler.RunIfDueAsync(TestContext.Current.CancellationToken);
        _orders.Host.Time.Advance(TimeSpan.FromHours(6));
        var due = await scheduler.RunIfDueAsync(TestContext.Current.CancellationToken);

        Assert.Equal((false, true), (early, due));
    }
}

/// <summary>The command reports a failed delivery with exit code 1.</summary>
public sealed class SendNotificationsFailureTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private readonly RecordingEmailSender _smtp = new() { FailNext = int.MaxValue };
    private OrderTestHost _orders = null!;

    public async ValueTask InitializeAsync() => _orders = await OrderTestHost.StartAsync(postgres, mailpit, configureServices: _smtp.Register);

    public async ValueTask DisposeAsync() => await _orders.DisposeAsync();

    [Fact]
    public async Task A_failed_delivery_makes_the_command_exit_with_one()
    {
        var admin = await _orders.Host.CreateUserAsync("admin.fallo.avisos@example.test", UserRole.Admin);
        var today = PolvorApp.SharedKernel.Time.FederationCalendar.Today(_orders.Host.Time);
        await _orders.Services.SaveEditionsAsync(new CalendarMilestone
        {
            Id = Guid.CreateVersion7(),
            EditionId = _orders.Current.Id,
            Date = today,
            Title = "Hito sintético",
            Notify = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        var command = _orders.Services.GetServices<IHostCommand>().Single(c => c.Verb == "send-notifications");

        var exitCode = await command.RunAsync([], TestContext.Current.CancellationToken);

        Assert.Equal(1, exitCode);
        Assert.Empty(_smtp.To(admin.Email));
    }
}
