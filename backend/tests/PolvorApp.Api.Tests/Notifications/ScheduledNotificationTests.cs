using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.FestivalEditions.Contracts;
using PolvorApp.FestivalEditions.Editions;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.Notifications.Contracts;
using PolvorApp.Notifications.Persistence;
using PolvorApp.Notifications.Scheduling;
using static PolvorApp.Api.Tests.Infrastructure.OrderData;
using static PolvorApp.Api.Tests.Infrastructure.RegistryData;

namespace PolvorApp.Api.Tests.Notifications;

/// <summary>
/// Specs "License digest", "Planned close reminders", "Milestone reminders" and "Scheduled notifications"
/// (design D5, D8–D10), run for a given date with SMTP replaced by a recording sender.
/// </summary>
public sealed class ScheduledNotificationTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private static readonly DateOnly NewYear = new(2031, 1, 1);
    private static readonly DateOnly CloseOn = new(2031, 2, 10);

    private readonly RecordingEmailSender _smtp = new();
    private OrderTestHost _orders = null!;
    private SyntheticUser _northChief = null!;
    private SyntheticUser _southChief = null!;
    private SyntheticUser _admin = null!;

    private IServiceProvider Services => _orders.Services;

    public async ValueTask InitializeAsync()
    {
        _orders = await OrderTestHost.StartAsync(postgres, mailpit, configureServices: _smtp.Register);
        await Services.OptOutOfEverythingAsync(_orders.Registry.AdminId, _orders.Registry.FiringChiefId);
        _northChief = await _orders.Host.CreateUserAsync("jefe.programados.norte@example.test");
        _southChief = await _orders.Host.CreateUserAsync("jefe.programados.sur@example.test");
        _admin = await _orders.Host.CreateUserAsync("admin.programados@example.test", UserRole.Admin);
        await _orders.Host.AssignAsync(_orders.Own.Id, _northChief.Id);
        await _orders.Host.AssignAsync(_orders.Other.Id, _southChief.Id);
    }

    public async ValueTask DisposeAsync() => await _orders.DisposeAsync();

    [Fact]
    public async Task The_monthly_digest_counts_license_problems_per_comparsa_once_a_month()
    {
        await AddLicensedAsync(_orders.Own.Id, expiresOn: new DateOnly(2031, 2, 15));
        await AddLicensedAsync(_orders.Own.Id, expiresOn: new DateOnly(2031, 8, 1));
        await AddLicensedAsync(_orders.Own.Id, pending: true);
        await AddLicensedAsync(_orders.Own.Id, expiresOn: new DateOnly(2030, 6, 1), status: ArquebusierStatus.Reserve);
        await AddLicensedAsync(_orders.Other.Id, expiresOn: new DateOnly(2035, 1, 1));

        var first = await Services.RunScheduledAsync(NewYear);
        var again = await Services.RunScheduledAsync(NewYear);
        var midMonth = await Services.RunScheduledAsync(new DateOnly(2031, 1, 15));
        await Services.PumpAsync();

        Assert.Equal((1, 0, 0), (first.Digests, again.Digests, midMonth.Digests));
        var digest = Assert.Single(_smtp.To(_northChief.Email));
        Assert.Equal("LicenseDigest", digest.Template);
        Assert.Contains($"{_orders.Own.Name}\n- Licencia en trámite: 1\n- Caduca en los próximos 90 días: 1", digest.TextBody, StringComparison.Ordinal);
        Assert.DoesNotContain("Arcabucero", digest.TextBody, StringComparison.Ordinal);
        Assert.Empty(_smtp.To(_southChief.Email));
        Assert.Empty(_smtp.To(_admin.Email));
    }

    [Fact]
    public async Task A_digest_missed_on_the_first_day_is_sent_on_the_next_run_of_the_month()
    {
        await AddLicensedAsync(_orders.Own.Id, license: false);

        var second = await Services.RunScheduledAsync(new DateOnly(2031, 3, 2));
        var later = await Services.RunScheduledAsync(new DateOnly(2031, 3, 20));

        Assert.Equal((1, 0), (second.Digests, later.Digests));
        Assert.Contains((await Services.DeliveriesAsync()), d => d.Topic == "digest:2031-03");
    }

    [Fact]
    public async Task Two_scheduled_runs_at_once_create_each_digest_once()
    {
        await AddLicensedAsync(_orders.Own.Id, license: false);

        await Task.WhenAll(Services.RunScheduledAsync(NewYear), Services.RunScheduledAsync(NewYear), Services.RunScheduledAsync(NewYear));

        Assert.Single(await Services.DeliveriesAsync(), d => d.Topic == "digest:2031-01");
    }

    [Fact]
    public async Task One_digest_lists_each_of_several_comparsas()
    {
        await _orders.Host.AssignAsync(_orders.Other.Id, _northChief.Id);
        await AddLicensedAsync(_orders.Own.Id, license: false);
        await AddLicensedAsync(_orders.Other.Id, expiresOn: new DateOnly(2030, 12, 1));

        await Services.RunScheduledAsync(NewYear);
        await Services.PumpAsync();

        var digest = Assert.Single(_smtp.To(_northChief.Email));
        Assert.Contains($"{_orders.Own.Name}\n- Sin licencia: 1", digest.TextBody, StringComparison.Ordinal);
        Assert.Contains($"{_orders.Other.Name}\n- Licencia caducada: 1", digest.TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Close_reminders_go_to_comparsas_whose_order_is_not_submitted()
    {
        await Services.SetOrdersCloseOnAsync(_orders.Current.Id, CloseOn);
        await Services.SaveOrdersAsync(NewOrder(_orders.Current, _orders.Own.Id), NewOrder(_orders.Current, _orders.Other.Id, OrderStatus.Submitted));

        var week = await Services.RunScheduledAsync(CloseOn.AddDays(-7));
        var weekAgain = await Services.RunScheduledAsync(CloseOn.AddDays(-5));
        var lastDay = await Services.RunScheduledAsync(CloseOn.AddDays(-1));
        await Services.PumpAsync();

        Assert.Equal((1, 0, 1), (week.CloseReminders, weekAgain.CloseReminders, lastDay.CloseReminders));
        Assert.Equal(["OrdersClosingSoon", "OrdersClosingTomorrow"], _smtp.To(_northChief.Email).Select(m => m.Template).Order());
        Assert.Contains("está en borrador", _smtp.To(_northChief.Email)[0].TextBody, StringComparison.Ordinal);
        Assert.Empty(_smtp.To(_southChief.Email));
    }

    [Fact]
    public async Task A_longer_close_lead_time_sends_the_first_reminder_earlier()
    {
        await SetLeadTimesAsync(closeReminderLeadDays: 10, milestoneLeadDays: 7);
        await Services.SetOrdersCloseOnAsync(_orders.Current.Id, CloseOn);
        await Services.SaveOrdersAsync(NewOrder(_orders.Current, _orders.Own.Id), NewOrder(_orders.Current, _orders.Other.Id, OrderStatus.Submitted));

        var tooEarly = await Services.RunScheduledAsync(CloseOn.AddDays(-11));
        var tenDays = await Services.RunScheduledAsync(CloseOn.AddDays(-10));
        await Services.PumpAsync();

        Assert.Equal((0, 1), (tooEarly.CloseReminders, tenDays.CloseReminders));
        Assert.Equal(["OrdersClosingSoon"], _smtp.To(_northChief.Email).Select(m => m.Template));
    }

    [Fact]
    public async Task A_lead_time_changed_between_runs_is_used_at_once_and_never_repeats_a_reminder()
    {
        await SetLeadTimesAsync(closeReminderLeadDays: 3, milestoneLeadDays: 7);
        await Services.SetOrdersCloseOnAsync(_orders.Current.Id, CloseOn);
        await Services.SaveOrdersAsync(NewOrder(_orders.Current, _orders.Own.Id), NewOrder(_orders.Current, _orders.Other.Id, OrderStatus.Submitted));

        var shortLead = await Services.RunScheduledAsync(CloseOn.AddDays(-5));
        await SetLeadTimesAsync(closeReminderLeadDays: 6, milestoneLeadDays: 7);
        var longerLead = await Services.RunScheduledAsync(CloseOn.AddDays(-5));
        await SetLeadTimesAsync(closeReminderLeadDays: 14, milestoneLeadDays: 7);
        var longestLead = await Services.RunScheduledAsync(CloseOn.AddDays(-4));

        // The new lead time is read by the next run; the reminder already sent is not sent again.
        Assert.Equal((0, 1, 0), (shortLead.CloseReminders, longerLead.CloseReminders, longestLead.CloseReminders));
        Assert.Single(await Services.DeliveriesAsync(), d => d.Topic.StartsWith("close7:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_moved_close_date_makes_the_reminder_due_again_and_closed_orders_get_none()
    {
        await Services.SetOrdersCloseOnAsync(_orders.Current.Id, CloseOn);
        await Services.RunScheduledAsync(CloseOn.AddDays(-7));
        var moved = CloseOn.AddDays(7);
        await Services.SetOrdersCloseOnAsync(_orders.Current.Id, moved);

        var afterMove = await Services.RunScheduledAsync(CloseOn);
        await _orders.SetOrdersOpenAsync(false);
        var closed = await Services.RunScheduledAsync(moved.AddDays(-1));

        Assert.Equal((2, 0), (afterMove.CloseReminders, closed.CloseReminders));
        Assert.Equal(4, (await Services.DeliveriesAsync()).Count(d => d.Topic.StartsWith("close7:", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task A_reminder_whose_orders_closed_before_sending_is_skipped()
    {
        await Services.SetOrdersCloseOnAsync(_orders.Current.Id, CloseOn);
        await Services.RunScheduledAsync(CloseOn.AddDays(-7));
        await _orders.SetOrdersOpenAsync(false);

        var result = await Services.PumpAsync();

        Assert.Equal((0, 2), (result.Sent, result.Skipped));
    }

    [Fact]
    public async Task Milestones_remind_admins_and_firing_chiefs_of_the_edition_in_progress_only()
    {
        var draft = NewEdition(2032, EditionStatus.Draft);
        await Services.SaveEditionsAsync(
            draft,
            NewMilestone(_orders.Current.Id, new DateOnly(2031, 1, 8), "Plazo sintético", notify: true),
            NewMilestone(_orders.Current.Id, new DateOnly(2031, 1, 5), "Sin aviso", notify: false),
            NewMilestone(_orders.Current.Id, new DateOnly(2031, 1, 9), "Fuera de plazo", notify: true),
            NewMilestone(draft.Id, new DateOnly(2031, 1, 3), "Hito de borrador", notify: true));

        var run = await Services.RunScheduledAsync(NewYear);
        await Services.PumpAsync();

        Assert.Equal(4, run.MilestoneReminders);
        Assert.Equal(2, _smtp.To(_admin.Email).Count);
        Assert.Equal("MilestoneReminder", Assert.Single(_smtp.To(_northChief.Email)).Template);
        Assert.Contains("«Plazo sintético»", _smtp.To(_northChief.Email)[0].TextBody, StringComparison.Ordinal);
        Assert.Single(_smtp.To(_southChief.Email));
    }

    [Fact]
    public async Task A_shorter_milestone_lead_time_waits_until_the_milestone_is_that_close()
    {
        await SetLeadTimesAsync(closeReminderLeadDays: 7, milestoneLeadDays: 3);
        await Services.SaveEditionsAsync(NewMilestone(_orders.Current.Id, new DateOnly(2031, 1, 6), "Plazo sintético", notify: true));

        var fiveDays = await Services.RunScheduledAsync(NewYear);
        var threeDays = await Services.RunScheduledAsync(new DateOnly(2031, 1, 3));

        Assert.Equal((0, 3), (fiveDays.MilestoneReminders, threeDays.MilestoneReminders));
    }

    [Fact]
    public async Task A_moved_milestone_is_reminded_again_and_one_turned_off_is_skipped()
    {
        var milestone = NewMilestone(_orders.Current.Id, new DateOnly(2031, 1, 8), "Plazo sintético", notify: true);
        await Services.SaveEditionsAsync(milestone);
        await Services.RunScheduledAsync(NewYear);
        await Services.PumpAsync();
        await UpdateMilestoneAsync(milestone.Id, new DateOnly(2031, 1, 14), notify: true);

        var moved = await Services.RunScheduledAsync(new DateOnly(2031, 1, 7));
        await UpdateMilestoneAsync(milestone.Id, new DateOnly(2031, 1, 14), notify: false);
        var sent = await Services.PumpAsync();

        Assert.Equal(3, moved.MilestoneReminders);
        Assert.Equal((0, 3), (sent.Sent, sent.Skipped));
        Assert.Single(_smtp.To(_northChief.Email));
    }

    [Fact]
    public async Task Each_kind_turned_off_stops_only_its_own_emails()
    {
        await AddLicensedAsync(_orders.Own.Id, license: false);
        await Services.SetOrdersCloseOnAsync(_orders.Current.Id, new DateOnly(2031, 1, 6));
        await Services.SaveEditionsAsync(NewMilestone(_orders.Current.Id, new DateOnly(2031, 1, 4), "Plazo sintético", notify: true));
        await OptOutAsync(_northChief.Id, NotificationKind.LicenseDigest);
        await OptOutAsync(_southChief.Id, NotificationKind.MilestoneReminder);
        await OptOutAsync(_admin.Id, NotificationKind.MilestoneReminder);

        await Services.RunScheduledAsync(NewYear);
        await Services.PumpAsync();

        Assert.Equal(["MilestoneReminder", "OrdersClosingSoon"], _smtp.To(_northChief.Email).Select(m => m.Template).Order());
        Assert.Equal(["OrdersClosingSoon"], _smtp.To(_southChief.Email).Select(m => m.Template));
        Assert.Empty(_smtp.To(_admin.Email));
    }

    [Fact]
    public async Task A_firing_chief_who_became_an_admin_keeps_milestone_reminders_off()
    {
        await OptOutAsync(_northChief.Id, NotificationKind.MilestoneReminder);
        await ChangeRoleAsync(_northChief.Id, UserRole.Admin);
        await Services.SaveEditionsAsync(NewMilestone(_orders.Current.Id, new DateOnly(2031, 1, 4), "Plazo sintético", notify: true));

        await Services.RunScheduledAsync(NewYear);
        await Services.PumpAsync();

        Assert.Empty(_smtp.To(_northChief.Email));
        Assert.Single(_smtp.To(_admin.Email));
    }

    [Fact]
    public async Task Close_reminders_skip_validated_orders_and_remind_returned_ones()
    {
        await Services.SetOrdersCloseOnAsync(_orders.Current.Id, CloseOn);
        var returned = NewOrder(_orders.Current, _orders.Own.Id, OrderStatus.Returned);
        returned.ReturnReason = "Motivo sintético";
        await Services.SaveOrdersAsync(returned, NewOrder(_orders.Current, _orders.Other.Id, OrderStatus.Validated));

        var run = await Services.RunScheduledAsync(CloseOn.AddDays(-3));
        await Services.PumpAsync();

        Assert.Equal(1, run.CloseReminders);
        Assert.Contains("está devuelto y pendiente de corregir", Assert.Single(_smtp.To(_northChief.Email)).TextBody, StringComparison.Ordinal);
        Assert.DoesNotContain("Motivo sintético", _smtp.To(_northChief.Email)[0].TextBody, StringComparison.Ordinal);
        Assert.Empty(_smtp.To(_southChief.Email));
    }

    [Fact]
    public async Task Milestones_of_a_closed_edition_or_already_past_are_not_reminded()
    {
        await Services.SaveEditionsAsync(
            NewMilestone(_orders.Previous.Id, new DateOnly(2031, 1, 3), "Hito de edición cerrada", notify: true),
            NewMilestone(_orders.Current.Id, new DateOnly(2030, 12, 31), "Hito pasado", notify: true));

        var run = await Services.RunScheduledAsync(NewYear);

        Assert.Equal(0, run.MilestoneReminders);
    }

    [Fact]
    public async Task The_scheduled_date_is_the_madrid_date_not_the_utc_date()
    {
        await AddLicensedAsync(_orders.Own.Id, license: false);
        var scheduler = Services.GetRequiredService<NotificationScheduler>();

        // 23:30 UTC on 31 December is 00:30 on 1 January in Madrid: the new year, but before 08:00.
        _orders.Host.Time.SetUtcNow(new DateTimeOffset(2030, 12, 31, 23, 30, 0, TimeSpan.Zero));
        var early = await scheduler.RunIfDueAsync(TestContext.Current.CancellationToken);
        _orders.Host.Time.SetUtcNow(new DateTimeOffset(2031, 1, 1, 7, 30, 0, TimeSpan.Zero));
        var due = await scheduler.RunIfDueAsync(TestContext.Current.CancellationToken);

        Assert.Equal((false, true), (early, due));
        Assert.Contains(await Services.DeliveriesAsync(), d => d.Topic == "digest:2031-01");
    }

    [Fact]
    public async Task The_clean_up_removes_old_deliveries_and_processed_events()
    {
        var now = _orders.Host.Time.GetUtcNow();
        await using (var scope = Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
            db.Events.AddRange(
                Event(now.AddDays(-31), processed: true),
                Event(now.AddDays(-31), processed: false),
                Event(now.AddDays(-1), processed: true));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            await db.TryAddAsync(Delivery(now.AddDays(-366), "viejo"), TestContext.Current.CancellationToken);
            await db.TryAddAsync(Delivery(now.AddDays(-300), "reciente"), TestContext.Current.CancellationToken);
        }

        await Services.RunScheduledAsync(NewYear);

        await using var check = Services.CreateAsyncScope();
        var context = check.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        Assert.Equal(["reciente"], await context.Deliveries.Select(d => d.Topic).ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2, await context.Events.CountAsync(TestContext.Current.CancellationToken));
    }

    private async Task AddLicensedAsync(
        Guid comparsaId, DateOnly? expiresOn = null, bool pending = false, bool license = true, ArquebusierStatus status = ArquebusierStatus.Active)
    {
        var arquebusier = NewArquebusier(comparsaId);
        arquebusier.Status = status;
        if (license)
        {
            arquebusier.LicenseType = LicenseType.Ae;
            arquebusier.LicensePending = pending;
            arquebusier.LicenseIssuedOn = pending || expiresOn is null ? null : new DateOnly(2025, 1, 1);
            arquebusier.LicenseExpiresOn = pending ? null : expiresOn;
        }

        await Services.SaveRegistryAsync(arquebusier);
    }

    private async Task SetLeadTimesAsync(int closeReminderLeadDays, int milestoneLeadDays)
    {
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<FederationCatalog.Persistence.FederationCatalogDbContext>().FederationSettings.ExecuteUpdateAsync(
            s => s.SetProperty(x => x.CloseReminderLeadDays, closeReminderLeadDays).SetProperty(x => x.MilestoneLeadDays, milestoneLeadDays),
            TestContext.Current.CancellationToken);
    }

    private async Task OptOutAsync(Guid userId, NotificationKind kind)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        db.OptOuts.Add(new NotificationOptOut { UserId = userId, Kind = kind, CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task ChangeRoleAsync(Guid userId, UserRole role)
    {
        await using var scope = Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<IdentityAccess.Users.User>>();
        var user = await users.FindByIdAsync(userId.ToString());
        user!.Role = role;
        await users.UpdateAsync(user);
    }

    private async Task UpdateMilestoneAsync(Guid id, DateOnly date, bool notify)
    {
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<FestivalEditions.Persistence.FestivalEditionsDbContext>().Milestones.Where(m => m.Id == id)
            .ExecuteUpdateAsync(m => m.SetProperty(x => x.Date, date).SetProperty(x => x.Notify, notify), TestContext.Current.CancellationToken);
    }

    private static CalendarMilestone NewMilestone(Guid editionId, DateOnly date, string title, bool notify) => new()
    {
        Id = Guid.CreateVersion7(),
        EditionId = editionId,
        Date = date,
        Title = title,
        Notify = notify,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static NotificationEventEntry Event(DateTimeOffset at, bool processed) => new()
    {
        Id = Guid.CreateVersion7(),
        Type = NotificationEventType.OrdersOpened,
        EditionId = Guid.CreateVersion7(),
        OccurredAt = at,
        ProcessedAt = processed ? at : null,
    };

    private static NotificationDelivery Delivery(DateTimeOffset createdAt, string topic) => new()
    {
        Id = Guid.CreateVersion7(),
        UserId = Guid.CreateVersion7(),
        Kind = NotificationKind.OrderStatus,
        Template = "OrderValidated",
        Topic = topic,
        Data = "{}",
        Status = DeliveryStatus.Sent,
        NextAttemptAt = createdAt,
        CreatedAt = createdAt,
    };
}
