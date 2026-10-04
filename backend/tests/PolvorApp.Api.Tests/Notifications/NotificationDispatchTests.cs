using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.FederationCatalog.Persistence;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.IdentityAccess.Users;
using PolvorApp.Notifications.Contracts;
using PolvorApp.Notifications.Persistence;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Notifications;

/// <summary>
/// Event emails from end to end (specs: Notification kinds and recipients, Orders opened and closed,
/// Order status emails, Notification delivery; design D5, D6): who receives what, re-checks at send time,
/// retries and their limit. SMTP is replaced by a recording sender.
/// </summary>
public sealed class NotificationDispatchTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private readonly RecordingEmailSender _smtp = new();
    private OrderTestHost _orders = null!;
    private SyntheticUser _ownChief = null!;
    private SyntheticUser _secondChief = null!;
    private SyntheticUser _otherChief = null!;
    private SyntheticUser _admin = null!;

    private IServiceProvider Services => _orders.Services;

    public async ValueTask InitializeAsync()
    {
        _orders = await OrderTestHost.StartAsync(postgres, mailpit, configureServices: _smtp.Register);
        await Services.OptOutOfEverythingAsync(_orders.Registry.AdminId, _orders.Registry.FiringChiefId);
        await Services.SetOrdersCloseOnAsync(_orders.Current.Id, new DateOnly(2031, 2, 10));
        _ownChief = await _orders.Host.CreateUserAsync("jefe.avisos.propio@example.test", locale: "ca-ES-valencia");
        _secondChief = await _orders.Host.CreateUserAsync("jefe.avisos.segundo@example.test");
        _otherChief = await _orders.Host.CreateUserAsync("jefe.avisos.otro@example.test", locale: "en");
        _admin = await _orders.Host.CreateUserAsync("admin.avisos@example.test", UserRole.Admin);
        await _orders.Host.AssignAsync(_orders.Own.Id, _ownChief.Id);
        await _orders.Host.AssignAsync(_orders.Own.Id, _secondChief.Id);
        await _orders.Host.AssignAsync(_orders.Other.Id, _otherChief.Id);
    }

    public async ValueTask DisposeAsync() => await _orders.DisposeAsync();

    [Fact]
    public async Task A_return_reaches_the_comparsas_firing_chiefs_only_in_their_language()
    {
        var order = await SubmittedOrderAsync();

        await MoveAsync(_orders.Admin, order, "return", new { reason = "Motivo sintético que no se envía" });
        var result = await Services.PumpAsync();

        var valencian = Assert.Single(_smtp.To(_ownChief.Email));
        Assert.Equal("OrderReturned", valencian.Template);
        Assert.StartsWith("Comanda retornada", valencian.Subject, StringComparison.Ordinal);
        Assert.Contains($"/orders/{Id(order)}", valencian.TextBody, StringComparison.Ordinal);
        Assert.DoesNotContain("Motivo sintético", valencian.TextBody, StringComparison.Ordinal);
        Assert.Single(_smtp.To(_secondChief.Email));
        Assert.Empty(_smtp.To(_otherChief.Email));
        Assert.Empty(_smtp.To(_admin.Email));
        Assert.Empty(_smtp.To("jefe.registro@example.test"));
        Assert.Equal(2, result.Sent);
    }

    [Fact]
    public async Task A_firing_chiefs_submission_reaches_the_admins()
    {
        var order = await _orders.PrepareAsync(_orders.FiringChief, _orders.Own.Id);

        await MoveAsync(_orders.FiringChief, order, "submit", new { attestation = true });
        await Services.PumpAsync();

        var email = Assert.Single(_smtp.To(_admin.Email));
        Assert.Equal("OrderSubmitted", email.Template);
        Assert.Empty(_smtp.To(_ownChief.Email));
    }

    [Fact]
    public async Task Opening_the_orders_reaches_every_firing_chief_with_an_active_comparsa()
    {
        var invited = await _orders.Host.CreateUserAsync("jefe.avisos.invitado@example.test", withPassword: false, enrolled: false);
        var unassigned = await _orders.Host.CreateUserAsync("jefe.avisos.sinasignar@example.test");
        await _orders.Host.AssignAsync(_orders.Own.Id, invited.Id);
        await _orders.SetOrdersOpenAsync(false);

        await SetOrdersAsync(open: true);
        await Services.PumpAsync();

        Assert.Equal("OrdersOpened", Assert.Single(_smtp.To(_ownChief.Email)).Template);
        Assert.Single(_smtp.To(_secondChief.Email));
        Assert.Single(_smtp.To(_otherChief.Email));
        Assert.Empty(_smtp.To(invited.Email));
        Assert.Empty(_smtp.To(unassigned.Email));
        Assert.Empty(_smtp.To(_admin.Email));
    }

    [Fact]
    public async Task Turning_off_order_status_stops_those_emails_only()
    {
        await using (var scope = Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
            db.OptOuts.Add(new NotificationOptOut { UserId = _ownChief.Id, Kind = NotificationKind.OrderStatus, CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var order = await SubmittedOrderAsync();
        await MoveAsync(_orders.Admin, order, "validate", new { });
        await _orders.SetOrdersOpenAsync(false);
        await SetOrdersAsync(open: true);
        await Services.PumpAsync();

        Assert.Equal(["OrdersOpened"], _smtp.To(_ownChief.Email).Select(m => m.Template));
        Assert.Equal(["OrderValidated", "OrdersOpened"], _smtp.To(_secondChief.Email).Select(m => m.Template).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task An_opted_out_firing_chief_gets_no_email()
    {
        await Services.OptOutOfEverythingAsync(_secondChief.Id);
        var order = await SubmittedOrderAsync();

        await MoveAsync(_orders.Admin, order, "validate", new { });
        await Services.PumpAsync();

        Assert.Single(_smtp.To(_ownChief.Email));
        Assert.Empty(_smtp.To(_secondChief.Email));
    }

    [Fact]
    public async Task Recipients_are_checked_again_when_the_email_is_sent()
    {
        var order = await SubmittedOrderAsync();
        await MoveAsync(_orders.Admin, order, "validate", new { });
        await Services.ExpandAsync();

        await DeactivateAsync(_ownChief.Id);
        await UnassignAsync(_secondChief.Id, _orders.Own.Id);
        var result = await Services.SendDueAsync();

        Assert.Empty(_smtp.To(_ownChief.Email));
        Assert.Empty(_smtp.To(_secondChief.Email));
        Assert.Equal((0, 2), (result.Sent, result.Skipped));
        Assert.All(await Services.DeliveriesAsync(), d => Assert.Equal(DeliveryStatus.Skipped, d.Status));
    }

    [Fact]
    public async Task A_role_change_before_sending_drops_an_email_meant_for_the_other_role()
    {
        var order = await SubmittedOrderAsync();
        await MoveAsync(_orders.Admin, order, "validate", new { });
        await Services.ExpandAsync();

        await ChangeRoleAsync(_ownChief.Id, UserRole.Admin);
        await Services.SendDueAsync();

        Assert.Empty(_smtp.To(_ownChief.Email));
        Assert.Single(_smtp.To(_secondChief.Email));
        Assert.Contains(await Services.DeliveriesAsync(), d => d.UserId == _ownChief.Id && d.Status == DeliveryStatus.Skipped && d.LastError == "RoleChanged");
    }

    [Fact]
    public async Task A_firing_chief_unassigned_from_every_comparsa_gets_no_orders_open_email()
    {
        await _orders.SetOrdersOpenAsync(false);
        await SetOrdersAsync(open: true);
        await Services.ExpandAsync();

        await UnassignAsync(_otherChief.Id, _orders.Other.Id);
        await Services.SendDueAsync();

        Assert.Empty(_smtp.To(_otherChief.Email));
        Assert.Single(_smtp.To(_ownChief.Email));
    }

    [Fact]
    public async Task Toggling_the_orders_quickly_sends_one_email_for_the_latest_state()
    {
        await _orders.SetOrdersOpenAsync(false);
        await SetOrdersAsync(open: true);
        _orders.Host.Time.Advance(TimeSpan.FromSeconds(1));
        await SetOrdersAsync(open: false);
        _orders.Host.Time.Advance(TimeSpan.FromSeconds(1));
        await SetOrdersAsync(open: true);

        await Services.PumpAsync();

        Assert.Equal(["OrdersOpened"], _smtp.To(_ownChief.Email).Select(m => m.Template));
    }

    [Fact]
    public async Task An_event_that_cannot_be_planned_is_set_aside_and_the_others_are_sent()
    {
        await using (var scope = Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
            db.Events.Add(new NotificationEventEntry
            {
                Id = Guid.CreateVersion7(),
                Type = NotificationEventType.OrderReturned,
                EditionId = _orders.Current.Id,
                OccurredAt = _orders.Host.Time.GetUtcNow().AddMinutes(-1),
            });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var order = await SubmittedOrderAsync();
        await MoveAsync(_orders.Admin, order, "validate", new { });
        await Services.PumpAsync();

        Assert.Single(_smtp.To(_ownChief.Email));
        await using var check = Services.CreateAsyncScope();
        Assert.False(await check.ServiceProvider.GetRequiredService<NotificationsDbContext>().Events.AnyAsync(e => e.ProcessedAt == null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task An_email_that_no_longer_holds_is_not_sent()
    {
        await _orders.SetOrdersOpenAsync(false);
        await SetOrdersAsync(open: true);
        _orders.Host.Time.Advance(TimeSpan.FromSeconds(1));
        await SetOrdersAsync(open: false);

        await Services.PumpAsync();

        Assert.Equal(["OrdersClosed"], _smtp.To(_ownChief.Email).Select(m => m.Template));
    }

    [Fact]
    public async Task A_status_changed_again_before_sending_drops_the_stale_email()
    {
        var order = await SubmittedOrderAsync();
        order = await MoveAsync(_orders.Admin, order, "return", new { reason = "Corregir" });
        await Services.ExpandAsync();
        await _orders.ReadOrdersAsync(db => db.Orders.Where(o => o.Id == Id(order))
            .ExecuteUpdateAsync(o => o.SetProperty(x => x.Status, OrderStatus.Draft).SetProperty(x => x.ReturnReason, (string?)null), TestContext.Current.CancellationToken));

        await Services.SendDueAsync();

        Assert.Empty(_smtp.Sent);
    }

    [Fact]
    public async Task A_failed_delivery_is_retried_and_arrives_once()
    {
        var order = await SubmittedOrderAsync();
        await MoveAsync(_orders.Admin, order, "validate", new { });
        await Services.ExpandAsync();
        _smtp.FailNext = 2;

        var first = await Services.SendDueAsync();
        _orders.Host.Time.Advance(TimeSpan.FromMinutes(2));
        var second = await Services.SendDueAsync();

        Assert.Equal((0, 2), (first.Sent, first.Retried));
        Assert.Equal(2, second.Sent);
        Assert.Single(_smtp.To(_ownChief.Email));
        Assert.Single(_smtp.To(_secondChief.Email));
        Assert.All(await Services.DeliveriesAsync(), d => Assert.Equal((DeliveryStatus.Sent, 2), (d.Status, d.Attempts)));
    }

    [Fact]
    public async Task A_delivery_failing_for_a_day_is_given_up()
    {
        var order = await SubmittedOrderAsync();
        await MoveAsync(_orders.Admin, order, "validate", new { });
        await Services.ExpandAsync();
        _smtp.FailNext = int.MaxValue;

        for (var hour = 0; hour <= 30; hour++)
        {
            await Services.SendDueAsync();
            _orders.Host.Time.Advance(TimeSpan.FromHours(1));
        }

        _smtp.FailNext = 0;
        await Services.SendDueAsync();

        Assert.Empty(_smtp.Sent);
        var deliveries = await Services.DeliveriesAsync();
        Assert.All(deliveries, d => Assert.Equal((DeliveryStatus.Failed, "SMTP delivery failed"), (d.Status, d.LastError)));
        Assert.All(deliveries, d => Assert.True(d.Attempts >= 8));
    }

    [Fact]
    public async Task A_lease_abandoned_by_a_crash_is_retried_after_it_expires()
    {
        var order = await SubmittedOrderAsync();
        await MoveAsync(_orders.Admin, order, "validate", new { });
        await Services.ExpandAsync();
        await LeaseAllAsync();

        var during = await Services.SendDueAsync();
        _orders.Host.Time.Advance(TimeSpan.FromMinutes(11));
        var after = await Services.SendDueAsync();

        Assert.Equal((0, 2), (during.Claimed, after.Sent));
    }

    [Fact]
    public async Task Two_expanders_at_once_create_each_delivery_once()
    {
        var order = await SubmittedOrderAsync();
        await MoveAsync(_orders.Admin, order, "validate", new { });

        await Task.WhenAll(Services.ExpandAsync(), Services.ExpandAsync(), Services.ExpandAsync());

        var deliveries = await Services.DeliveriesAsync();
        Assert.Equal(2, deliveries.Count);
        Assert.Equal(new[] { _ownChief.Id, _secondChief.Id }.Order(), deliveries.Select(d => d.UserId).Order());
    }

    [Fact]
    public async Task Deliveries_hold_no_address_name_or_reason()
    {
        var order = await SubmittedOrderAsync();
        await MoveAsync(_orders.Admin, order, "return", new { reason = "Motivo sintético privado" });
        await Services.ExpandAsync();

        var delivery = (await Services.DeliveriesAsync()).First();

        Assert.DoesNotContain("example.test", delivery.Data, StringComparison.Ordinal);
        Assert.DoesNotContain("Motivo", delivery.Data, StringComparison.Ordinal);
        Assert.DoesNotContain("Sintética", delivery.Data, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unreachable_smtp_server_does_not_affect_the_change_and_the_email_comes_later()
    {
        _smtp.FailNext = int.MaxValue;
        var order = await SubmittedOrderAsync();

        using var response = await PostAsync(_orders.Admin, order, "validate", new { });
        await Services.PumpAsync();
        _smtp.FailNext = 0;
        _orders.Host.Time.Advance(TimeSpan.FromMinutes(2));
        await Services.PumpAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(_smtp.To(_ownChief.Email));
    }

    private async Task<JsonElement> SubmittedOrderAsync()
    {
        var order = await _orders.PrepareAsync(_orders.Admin, _orders.Own.Id);
        return await MoveAsync(_orders.Admin, order, "submit", new { });
    }

    private async Task SetOrdersAsync(bool open)
    {
        var version = (await _orders.Admin.GetEditionAsync(_orders.Current.Id)).Version;
        using var response = await _orders.Admin.PostAsync($"/api/editions/{_orders.Current.Id}/orders", new { open, version });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task DeactivateAsync(Guid userId)
    {
        await using var scope = Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var user = await users.FindByIdAsync(userId.ToString());
        user!.Active = false;
        await users.UpdateAsync(user);
    }

    private async Task ChangeRoleAsync(Guid userId, UserRole role)
    {
        await using var scope = Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var user = await users.FindByIdAsync(userId.ToString());
        user!.Role = role;
        await users.UpdateAsync(user);
    }

    private async Task UnassignAsync(Guid userId, Guid comparsaId)
    {
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<FederationCatalogDbContext>().Assignments
            .Where(a => a.UserId == userId && a.ComparsaId == comparsaId)
            .ExecuteDeleteAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Claims every due delivery as a sender that then crashed would have.</summary>
    private async Task LeaseAllAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var until = _orders.Host.Time.GetUtcNow().AddMinutes(10);
        await scope.ServiceProvider.GetRequiredService<NotificationsDbContext>().Deliveries
            .ExecuteUpdateAsync(d => d.SetProperty(x => x.NextAttemptAt, until), TestContext.Current.CancellationToken);
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
