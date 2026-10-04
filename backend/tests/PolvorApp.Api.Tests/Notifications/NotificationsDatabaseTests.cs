using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PolvorApp.Api.Platform.Database;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.Notifications.Contracts;
using PolvorApp.Notifications.Persistence;

namespace PolvorApp.Api.Tests.Notifications;

/// <summary>Design D2–D4: the schema of the outbox, the deliveries, the opt-outs and the scheduled runs.</summary>
public sealed class NotificationsDatabaseTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2031, 1, 1, 8, 0, 0, TimeSpan.Zero);

    private ApiFactory? _factory;

    public async ValueTask InitializeAsync()
    {
        _factory = new ApiFactory(await postgres.CreateMigratedDatabaseAsync());
        Assert.Equal(0, await MigrateCommand.RunAsync(_factory.Services, TestContext.Current.CancellationToken));
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
    }

    [Fact]
    public async Task The_three_tables_and_their_indexes_exist()
    {
        var indexes = await ScalarsAsync(
            "SELECT indexname AS \"Value\" FROM pg_indexes WHERE schemaname = 'notifications' ORDER BY indexname");

        Assert.Contains("ix_notification_events_pending", indexes);
        Assert.Contains(NotificationsDbContext.DeliveryTopicIndex, indexes);
        Assert.Contains("ix_notification_deliveries_due", indexes);
        Assert.Equal(
            ["notification_deliveries", "notification_events", "notification_opt_outs", "notification_runs"],
            await ScalarsAsync("SELECT table_name AS \"Value\" FROM information_schema.tables WHERE table_schema = 'notifications' AND table_name NOT LIKE '\\_\\_%' ORDER BY table_name"));
    }

    [Fact]
    public async Task A_second_delivery_for_the_same_user_and_topic_is_ignored()
    {
        var userId = Guid.CreateVersion7();
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

        var first = await db.TryAddAsync(NewDelivery(userId, "digest:2031-01"), TestContext.Current.CancellationToken);
        var again = await db.TryAddAsync(NewDelivery(userId, "digest:2031-01"), TestContext.Current.CancellationToken);
        var otherUser = await db.TryAddAsync(NewDelivery(Guid.CreateVersion7(), "digest:2031-01"), TestContext.Current.CancellationToken);

        Assert.Equal((true, false, true), (first, again, otherUser));
        var stored = await db.Deliveries.AsNoTracking().Where(d => d.UserId == userId).SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal((DeliveryStatus.Pending, 0, "{\"month\": \"2031-01\"}"), (stored.Status, stored.Attempts, stored.Data));
    }

    [Fact]
    public async Task An_unknown_kind_or_status_violates_the_checks()
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

        var kind = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
            "INSERT INTO notifications.notification_opt_outs (user_id, kind, created_at) VALUES (gen_random_uuid(), 'SMS_ALERTS', now())",
            TestContext.Current.CancellationToken));
        var type = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
            "INSERT INTO notifications.notification_events (id, type, edition_id, occurred_at) VALUES (gen_random_uuid(), 'ORDER_LOST', gen_random_uuid(), now())",
            TestContext.Current.CancellationToken));

        Assert.Equal((PostgresErrorCodes.CheckViolation, "ck_notification_opt_outs_kind"), (kind.SqlState, kind.ConstraintName));
        Assert.Equal((PostgresErrorCodes.CheckViolation, "ck_notification_events_type"), (type.SqlState, type.ConstraintName));
    }

    private static NotificationDelivery NewDelivery(Guid userId, string topic) => new()
    {
        Id = Guid.CreateVersion7(),
        UserId = userId,
        Kind = NotificationKind.LicenseDigest,
        Template = "LicenseDigest",
        Topic = topic,
        Data = "{\"month\":\"2031-01\"}",
        NextAttemptAt = Now,
        CreatedAt = Now,
    };

    private async Task<List<string>> ScalarsAsync(string sql)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<NotificationsDbContext>().Database
            .SqlQueryRaw<string>(sql).ToListAsync(TestContext.Current.CancellationToken);
    }
}
