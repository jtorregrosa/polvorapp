using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using PolvorApp.Api.Platform.Seeding;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.FestivalEditions.Persistence;
using PolvorApp.FestivalEditions.Seeding;
using PolvorApp.IdentityAccess.Users;
using PolvorApp.Notifications.Persistence;
using PolvorApp.Notifications.Seeding;
using PolvorApp.SharedKernel.Time;

namespace PolvorApp.Api.Tests.Notifications;

/// <summary>Spec "Synthetic notification data" (SEC-11): one milestone reminded by email, one kind off, nothing sent.</summary>
public sealed class NotificationSeederTests(PostgresFixture postgres, MailpitFixture mailpit, MinioFixture minio)
{
    private const string SeedPassword = "semilla-sintetica-local";
    private const string SeedKey = "JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP";

    [Fact]
    public async Task Seeding_twice_creates_the_reminder_milestone_and_the_opt_out_once_and_sends_nothing()
    {
        await using var host = await StartAsync();

        Assert.Equal(0, await SeedAsync(host));
        Assert.Equal(0, await SeedAsync(host));

        await using var scope = host.Services.CreateAsyncScope();
        var editions = scope.ServiceProvider.GetRequiredService<FestivalEditionsDbContext>();
        var notifications = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        var today = FederationCalendar.Today(host.Time);
        var reminded = await editions.Milestones.AsNoTracking().Where(m => m.Notify).ToListAsync(TestContext.Current.CancellationToken);
        var reminder = Assert.Single(reminded);
        Assert.Equal((EditionSeeder.ReminderMilestoneId, EditionSeeder.CurrentEdition), (reminder.Id, reminder.EditionId));
        Assert.InRange(reminder.Date.DayNumber - today.DayNumber, 0, 7);
        Assert.True(await editions.Milestones.AnyAsync(m => !m.Notify && m.EditionId == EditionSeeder.CurrentEdition, TestContext.Current.CancellationToken));
        var optOut = Assert.Single(await notifications.OptOuts.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal((NotificationSeeder.SeededChief, NotificationSeeder.SeededOptOut), (optOut.UserId, optOut.Kind));
        Assert.Empty(await notifications.Events.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await notifications.Deliveries.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Sending_after_seeding_reminds_the_seeded_milestone_to_those_who_want_it()
    {
        await using var host = await StartAsync();
        Assert.Equal(0, await SeedAsync(host));

        await host.Services.RunScheduledAsync(FederationCalendar.Today(host.Time));

        var reminders = (await host.Services.DeliveriesAsync()).Where(d => d.Template == "MilestoneReminder").ToList();
        Assert.Equal(3, reminders.Count); // the seeded Admin and both active FiringChiefs; not the invited or deactivated ones
        Assert.All(reminders, d => Assert.Contains(EditionSeeder.ReminderMilestoneId.ToString(), d.Topic, StringComparison.Ordinal));
    }

    private Task<IdentityTestHost> StartAsync()
    {
        var settings = new Dictionary<string, string?> { [IdentitySeeder.PasswordKey] = SeedPassword, [IdentitySeeder.AuthenticatorKeyKey] = SeedKey };
        foreach (var (key, value) in minio.SettingsFor("polvorapp-test-avisos"))
        {
            settings[key] = value;
        }

        return IdentityTestHost.StartAsync(postgres, mailpit, settings);
    }

    private static Task<int> SeedAsync(IdentityTestHost host) =>
        SeedCommand.RunAsync(host.Services, new HostingEnvironment { EnvironmentName = Environments.Development }, TestContext.Current.CancellationToken);
}
