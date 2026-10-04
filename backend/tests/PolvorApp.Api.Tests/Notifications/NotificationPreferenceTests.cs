using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.IdentityAccess.Users;
using PolvorApp.Notifications.Contracts;
using PolvorApp.Notifications.Preferences;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Notifications;

/// <summary>Specs "Notification preferences" and "Notification changes are audited" (design D4, D11).</summary>
public sealed class NotificationPreferenceTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private const string Path = "/api/account/notification-preferences";

    private IdentityTestHost _host = null!;

    public async ValueTask InitializeAsync() => _host = await IdentityTestHost.StartAsync(postgres, mailpit);

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public void Each_role_has_its_kinds_in_a_fixed_order()
    {
        Assert.Equal(
            [NotificationKind.LicenseDigest, NotificationKind.OrderWindow, NotificationKind.OrderStatus, NotificationKind.MilestoneReminder],
            NotificationKinds.For(UserRole.FiringChief));
        Assert.Equal([NotificationKind.OrderStatus, NotificationKind.MilestoneReminder], NotificationKinds.For(UserRole.Admin));
        Assert.False(NotificationKinds.AppliesTo(NotificationKind.LicenseDigest, UserRole.Admin));
    }

    [Fact]
    public async Task Every_kind_of_the_role_is_on_by_default()
    {
        using var chief = await SignInAsync("jefe.avisos.defecto@example.test", UserRole.FiringChief);
        using var admin = await SignInAsync("admin.avisos.defecto@example.test", UserRole.Admin);

        Assert.Equal(
            [("LICENSE_DIGEST", true), ("ORDER_WINDOW", true), ("ORDER_STATUS", true), ("MILESTONE_REMINDER", true)],
            await KindsAsync(chief));
        Assert.Equal([("ORDER_STATUS", true), ("MILESTONE_REMINDER", true)], await KindsAsync(admin));
    }

    [Fact]
    public async Task A_firing_chief_turns_a_kind_off_and_on_again_and_each_change_is_audited()
    {
        using var chief = await SignInAsync("jefe.avisos.cambio@example.test", UserRole.FiringChief);

        using var off = await SaveAsync(chief, ("LICENSE_DIGEST", false));
        using var unchanged = await SaveAsync(chief, ("LICENSE_DIGEST", false), ("ORDER_WINDOW", true));
        var afterOff = await KindsAsync(chief);
        // The host's clock is frozen: move it on, so the audit entries are told apart by time.
        _host.Time.Advance(TimeSpan.FromSeconds(1));
        using var on = await SaveAsync(chief, ("LICENSE_DIGEST", true));

        Assert.Equal(HttpStatusCode.OK, off.StatusCode);
        Assert.Equal(HttpStatusCode.OK, unchanged.StatusCode);
        Assert.Contains(("LICENSE_DIGEST", false), afterOff);
        Assert.Contains(("ORDER_WINDOW", true), afterOff);
        Assert.Contains(("LICENSE_DIGEST", true), await ReadKindsAsync(on));
        var entries = (await _host.AuditEntriesAsync(NotificationPreferences.AuditAction)).OrderBy(e => e.OccurredAt).ToList();
        Assert.Equal(2, entries.Count);
        using var first = JsonDocument.Parse(entries[0].Data!);
        using var second = JsonDocument.Parse(entries[1].Data!);
        Assert.Equal(["LICENSE_DIGEST"], first.RootElement.GetProperty("turnedOff").EnumerateArray().Select(e => e.GetString()));
        Assert.Empty(first.RootElement.GetProperty("turnedOn").EnumerateArray());
        Assert.Equal(["LICENSE_DIGEST"], second.RootElement.GetProperty("turnedOn").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(("User", entries[0].ActorUserId?.ToString()), (entries[0].EntityType, entries[0].EntityId));
    }

    [Fact]
    public async Task Kinds_left_out_keep_their_value()
    {
        using var chief = await SignInAsync("jefe.avisos.omitidos@example.test", UserRole.FiringChief);
        using var first = await SaveAsync(chief, ("ORDER_WINDOW", false));

        using var second = await SaveAsync(chief, ("MILESTONE_REMINDER", false));

        Assert.Equal(
            [("LICENSE_DIGEST", true), ("ORDER_WINDOW", false), ("ORDER_STATUS", true), ("MILESTONE_REMINDER", false)],
            await ReadKindsAsync(second));
    }

    [Theory]
    [InlineData("SMS_ALERTS", "invalid")]
    [InlineData("license_digest", "invalid")]
    [InlineData("LICENSE_DIGEST", "notApplicable")]
    public async Task An_invalid_or_inapplicable_kind_is_named(string kind, string reason)
    {
        using var admin = await SignInAsync("admin.avisos.invalido@example.test", UserRole.Admin);

        using var response = await SaveAsync(admin, (kind, false));

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation");
        Assert.Equal(new Dictionary<string, string> { ["kinds[0].kind"] = reason }, await ErrorsAsync(response));
        Assert.Empty(await _host.AuditEntriesAsync(NotificationPreferences.AuditAction));
    }

    [Fact]
    public async Task A_save_with_one_invalid_kind_changes_nothing()
    {
        using var admin = await SignInAsync("admin.avisos.mixto@example.test", UserRole.Admin);

        using var response = await SaveAsync(admin, ("ORDER_STATUS", false), ("LICENSE_DIGEST", false));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal([("ORDER_STATUS", true), ("MILESTONE_REMINDER", true)], await KindsAsync(admin));
    }

    [Fact]
    public async Task A_duplicate_kind_a_missing_value_and_a_missing_list_are_named()
    {
        using var chief = await SignInAsync("jefe.avisos.duplicado@example.test", UserRole.FiringChief);

        using var duplicate = await SaveAsync(chief, ("ORDER_STATUS", false), ("ORDER_STATUS", true));
        using var missing = await chief.PutAsJsonAsync(Path, new { kinds = new object[] { new { kind = "ORDER_STATUS" } } }, TestContext.Current.CancellationToken);
        using var noList = await chief.PutAsJsonAsync(Path, new { }, TestContext.Current.CancellationToken);

        Assert.Equal(new Dictionary<string, string> { ["kinds[1].kind"] = "duplicate" }, await ErrorsAsync(duplicate));
        Assert.Equal(new Dictionary<string, string> { ["kinds[0].enabled"] = "required" }, await ErrorsAsync(missing));
        Assert.Equal(new Dictionary<string, string> { ["kinds"] = "required" }, await ErrorsAsync(noList));
        Assert.All(await KindsAsync(chief), k => Assert.True(k.Enabled));
    }

    [Fact]
    public async Task A_role_change_keeps_the_opt_outs_and_lists_the_new_roles_kinds()
    {
        var user = await _host.CreateUserAsync("jefe.avisos.rol@example.test");
        using (var chief = await _host.SignInAsync(user))
        {
            using var off = await SaveAsync(chief, ("MILESTONE_REMINDER", false), ("LICENSE_DIGEST", false));
        }

        await using (var scope = _host.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var stored = await users.FindByIdAsync(user.Id.ToString());
            stored!.Role = UserRole.Admin;
            await users.UpdateAsync(stored);
        }

        using var admin = await _host.SignInAsync(user);

        Assert.Equal([("ORDER_STATUS", true), ("MILESTONE_REMINDER", false)], await KindsAsync(admin));
    }

    [Fact]
    public async Task An_anonymous_request_is_unauthorized()
    {
        using var anonymous = await _host.NewClientAsync();

        using var read = await anonymous.GetAsync(Path, TestContext.Current.CancellationToken);
        using var save = await anonymous.PutAsJsonAsync(Path, new { kinds = Array.Empty<object>() }, TestContext.Current.CancellationToken);

        Assert.Equal((HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized), (read.StatusCode, save.StatusCode));
    }

    private async Task<HttpClient> SignInAsync(string email, UserRole role) =>
        await _host.SignInAsync(await _host.CreateUserAsync(email, role));

    private static Task<HttpResponseMessage> SaveAsync(HttpClient client, params (string Kind, bool Enabled)[] kinds) =>
        client.PutAsJsonAsync(Path, new { kinds = kinds.Select(k => new { kind = k.Kind, enabled = k.Enabled }) }, TestContext.Current.CancellationToken);

    private static async Task<List<(string Kind, bool Enabled)>> KindsAsync(HttpClient client)
    {
        using var response = await client.GetAsync(Path, TestContext.Current.CancellationToken);
        return await ReadKindsAsync(response);
    }

    private static async Task<List<(string Kind, bool Enabled)>> ReadKindsAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadAsync<JsonElement>(response);
        return body.GetProperty("kinds").EnumerateArray()
            .Select(k => (k.GetProperty("kind").GetString()!, k.GetProperty("enabled").GetBoolean()))
            .ToList();
    }
}
