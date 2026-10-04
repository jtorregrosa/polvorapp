using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.FestivalEditions.Persistence;
using PolvorApp.Notifications.Contracts;
using PolvorApp.Notifications.Persistence;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Notifications;

/// <summary>
/// The notification outbox (design D2): events commit with the change that causes them, hold ids only,
/// and are recorded by the order window and order review operations (spec: Orders opened and closed,
/// Order status emails).
/// </summary>
public sealed class NotificationOutboxTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private OrderTestHost _orders = null!;

    public async ValueTask InitializeAsync() => _orders = await OrderTestHost.StartAsync(postgres, mailpit);

    public async ValueTask DisposeAsync() => await _orders.DisposeAsync();

    [Fact]
    public async Task An_event_recorded_in_another_modules_context_commits_with_its_change()
    {
        await using var scope = _orders.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FestivalEditionsDbContext>();
        var outbox = scope.ServiceProvider.GetRequiredService<INotificationOutbox>();

        outbox.Record(db, NotificationEvent.OrdersOpened(_orders.Current.Id));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var stored = Assert.Single(await EventsAsync());
        Assert.Equal((NotificationEventType.OrdersOpened, _orders.Current.Id, null, null, null), (stored.Type, stored.EditionId, stored.ComparsaId, stored.OrderId, stored.ProcessedAt));
    }

    [Fact]
    public async Task An_event_is_absent_after_a_rollback()
    {
        await using var scope = _orders.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FestivalEditionsDbContext>();
        var outbox = scope.ServiceProvider.GetRequiredService<INotificationOutbox>();

        await using (var transaction = await db.Database.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            outbox.Record(db, NotificationEvent.OrdersClosed(_orders.Current.Id));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            await transaction.RollbackAsync(TestContext.Current.CancellationToken);
        }

        Assert.Empty(await EventsAsync());
    }

    [Fact]
    public void Events_hold_identifiers_only()
    {
        Assert.Equal(
            ["ComparsaId", "EditionId", "Id", "OccurredAt", "OrderId", "ProcessedAt", "Type"],
            typeof(NotificationEventEntry).GetProperties().Select(p => p.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Opening_and_closing_the_orders_records_an_event_only_when_the_value_changes()
    {
        await _orders.SetOrdersOpenAsync(false);

        await SetOrdersAsync(open: true);
        await SetOrdersAsync(open: true);
        _orders.Host.Time.Advance(TimeSpan.FromSeconds(1));
        await SetOrdersAsync(open: false);

        Assert.Equal(
            [NotificationEventType.OrdersOpened, NotificationEventType.OrdersClosed],
            (await EventsAsync()).OrderBy(e => e.OccurredAt).ThenBy(e => e.Id).Select(e => e.Type));
    }

    [Fact]
    public async Task A_firing_chiefs_submission_records_an_event_and_an_admins_does_not()
    {
        var own = await _orders.PrepareAsync(_orders.FiringChief, _orders.Own.Id);
        var other = await _orders.PrepareAsync(_orders.Admin, _orders.Other.Id);

        await MoveAsync(_orders.FiringChief, own, "submit", new { attestation = true });
        await MoveAsync(_orders.Admin, other, "submit", new { });

        var stored = Assert.Single(await EventsAsync());
        Assert.Equal(
            (NotificationEventType.OrderSubmitted, _orders.Current.Id, _orders.Own.Id, Id(own)),
            (stored.Type, stored.EditionId, stored.ComparsaId, stored.OrderId));
    }

    [Fact]
    public async Task Returning_and_validating_record_their_events()
    {
        var order = await _orders.PrepareAsync(_orders.Admin, _orders.Own.Id);
        order = await MoveAsync(_orders.Admin, order, "submit", new { });

        order = await MoveAsync(_orders.Admin, order, "return", new { reason = "Revisar los frascos sintéticos" });
        _orders.Host.Time.Advance(TimeSpan.FromSeconds(1));
        await MoveAsync(_orders.Admin, order, "validate", new { });

        Assert.Equal(
            [NotificationEventType.OrderReturned, NotificationEventType.OrderValidated],
            (await EventsAsync()).OrderBy(e => e.OccurredAt).ThenBy(e => e.Id).Select(e => e.Type));
    }

    [Fact]
    public async Task A_rejected_move_records_nothing()
    {
        var order = await _orders.PrepareAsync(_orders.FiringChief, _orders.Own.Id);

        using var unattested = await PostAsync(_orders.FiringChief, order, "submit", new { attestation = false });
        using var invalid = await PostAsync(_orders.Admin, order, "return", new { reason = "No se puede" });

        Assert.Equal(HttpStatusCode.BadRequest, unattested.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, invalid.StatusCode);
        Assert.Empty(await EventsAsync());
    }

    private async Task<List<NotificationEventEntry>> EventsAsync()
    {
        await using var scope = _orders.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<NotificationsDbContext>().Events.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken);
    }

    private async Task SetOrdersAsync(bool open)
    {
        var version = (await _orders.Admin.GetEditionAsync(_orders.Current.Id)).Version;
        using var response = await _orders.Admin.PostAsync($"/api/editions/{_orders.Current.Id}/orders", new { open, version });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static Guid Id(JsonElement order) => Guid.Parse(order.GetProperty("id").GetString()!);

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, JsonElement order, string move, object body)
    {
        var json = JsonSerializer.SerializeToNode(body)!.AsObject();
        json["version"] = order.GetProperty("version").GetUInt32();
        return client.PostAsync($"/api/comparsa-orders/{Id(order)}/{move}", (JsonNode)json);
    }

    private static async Task<JsonElement> MoveAsync(HttpClient client, JsonElement order, string move, object body)
    {
        using var response = await PostAsync(client, order, move, body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadAsync<JsonElement>(response);
    }
}
