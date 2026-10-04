using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.AuditPrivacy.Contracts;
using PolvorApp.AuditPrivacy.Persistence;
using PolvorApp.AuditPrivacy.Privacy;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.IdentityAccess.Persistence;
using PolvorApp.Notifications.Contracts;
using PolvorApp.Notifications.Persistence;
using PolvorApp.SharedKernel.Auditing;

namespace PolvorApp.Api.Tests.Privacy;

/// <summary>
/// Spec audit-privacy "Exporting / Erasing a user's data", identity-access "Users and roles" (ERASED),
/// design D8: the identity, catalogue, notification and audit parts of a user's request.
/// </summary>
public sealed class UserPersonalDataTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private RegistryTestHost _registry = null!;

    public async ValueTask InitializeAsync() => _registry = await RegistryTestHost.StartAsync(postgres, mailpit);

    public async ValueTask DisposeAsync() => await _registry.DisposeAsync();

    [Fact]
    public async Task A_firing_chiefs_export_holds_profile_assignment_preferences_deliveries_and_activity_and_no_secret()
    {
        var chief = await ChiefWithNotificationsAsync("jefa.exportada@example.test");

        var parts = await RunAsync(r => r.ExportAsync(new PersonalDataSubject.UserAccount(chief.Id), TestContext.Current.CancellationToken));

        var sheets = parts.SelectMany(p => p.Sheets).ToDictionary(s => s.Code);
        var profile = Assert.Single(sheets["profile"].Rows);
        Assert.Equal(chief.Email, profile[sheets["profile"].Columns.ToList().IndexOf("email")]);
        Assert.DoesNotContain(sheets["profile"].Columns, c => c.Contains("password", StringComparison.OrdinalIgnoreCase) || c.Contains("key", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(["Comparsa Sintética Propia"], sheets["assignments"].Rows.Select(r => r[0]));
        Assert.Contains(sheets["notificationPreferences"].Rows, r => (string)r[0]! == "LICENSE_DIGEST" && (bool)r[1]! == false);
        Assert.Equal(2, sheets["notificationDeliveries"].Rows.Count);
        Assert.NotEmpty(sheets["activity"].Rows);
    }

    [Fact]
    public async Task An_erased_user_is_anonymised_keeps_their_id_and_cannot_sign_in()
    {
        var chief = await ChiefWithNotificationsAsync("jefa.borrada@example.test");
        var normalised = chief.Email.ToUpperInvariant();
        await InsertFailedSignInAsync(normalised);
        using var session = await _registry.Host.SignInAsync(chief);
        var actedBefore = (await ActivityAsync(chief.Id)).Count;

        var result = await RunAsync(r => r.EraseAsync(new PersonalDataSubject.UserAccount(chief.Id), Audit, TestContext.Current.CancellationToken));

        Assert.Equal((1, 1, 1, 2, 1), (
            result!.Counts["usersErased"], result.Counts["assignmentsRemoved"], result.Counts["notificationPreferencesRemoved"],
            result.Counts["notificationDeliveriesRemoved"], result.Counts["auditEntriesRedacted"]));
        using var read = await _registry.Admin.GetAsync($"/api/users/{chief.Id}", TestContext.Current.CancellationToken);
        var user = await IdentityAssertions.ReadAsync<JsonElement>(read);
        Assert.Equal("ERASED", user.GetProperty("status").GetString());
        Assert.Equal($"erased-{chief.Id:N}@erased.invalid", user.GetProperty("email").GetString());
        Assert.Empty(await _registry.Host.AssignedComparsasAsync(chief.Id));
        Assert.Equal(actedBefore, (await ActivityAsync(chief.Id)).Count);
        using var log = await _registry.Admin.GetAsync($"/api/audit-entries?actorUserId={chief.Id}", TestContext.Current.CancellationToken);
        var actor = (await IdentityAssertions.ReadAsync<JsonElement>(log)).GetProperty("items")[0].GetProperty("actor");
        Assert.Equal((true, JsonValueKind.Null), (actor.GetProperty("erased").GetBoolean(), actor.GetProperty("name").ValueKind));
        await using var scope = _registry.Services.CreateAsyncScope();
        var data = await scope.ServiceProvider.GetRequiredService<AuditDbContext>().Set<AuditEntry>().AsNoTracking()
            .Where(e => e.Data != null).Select(e => e.Data!).ToListAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain(data, d => d.Contains(normalised, StringComparison.OrdinalIgnoreCase));
        using var login = await (await _registry.Host.NewClientAsync()).PostAsJsonAsync(
            "/api/auth/login", new { email = chief.Email, password = IdentityTestHost.Password }, TestContext.Current.CancellationToken);
        await IdentityAssertions.AssertProblemAsync(login, HttpStatusCode.Unauthorized, "auth.invalidCredentials");
        // A session opened before the erasure ends with it (spec: Erasing a user's data).
        using var account = await session.GetAsync("/api/account", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, account.StatusCode);
    }

    [Fact]
    public async Task An_erased_user_cannot_be_erased_edited_reactivated_reinvited_or_reset()
    {
        var chief = await _registry.Host.CreateUserAsync("jefa.final@example.test");
        await RunAsync(r => r.EraseAsync(new PersonalDataSubject.UserAccount(chief.Id), Audit, TestContext.Current.CancellationToken));

        var again = await Assert.ThrowsAsync<PersonalDataErasureRefusedException>(
            () => RunAsync(r => r.EraseAsync(new PersonalDataSubject.UserAccount(chief.Id), Audit, TestContext.Current.CancellationToken)));
        using var edit = await _registry.Admin.PutAsJsonAsync(
            $"/api/users/{chief.Id}", new { name = "Otra", role = "FIRING_CHIEF", locale = "es-ES" }, TestContext.Current.CancellationToken);
        using var reactivate = await _registry.Admin.PostAsync($"/api/users/{chief.Id}/reactivate", null, TestContext.Current.CancellationToken);
        using var invite = await _registry.Admin.PostAsync($"/api/users/{chief.Id}/invitation", null, TestContext.Current.CancellationToken);
        using var reset = await _registry.Admin.PostAsync($"/api/users/{chief.Id}/two-factor/reset", null, TestContext.Current.CancellationToken);

        Assert.Equal(PersonalDataErasureRefusedException.AlreadyErased, again.Code);
        foreach (var response in new[] { edit, reactivate, invite, reset })
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Contains("users.erased", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Erased_users_are_listed_under_their_own_status()
    {
        var chief = await _registry.Host.CreateUserAsync("jefa.listada@example.test");
        await RunAsync(r => r.EraseAsync(new PersonalDataSubject.UserAccount(chief.Id), Audit, TestContext.Current.CancellationToken));

        using var erased = await _registry.Admin.GetAsync("/api/users?status=ERASED", TestContext.Current.CancellationToken);
        using var deactivated = await _registry.Admin.GetAsync("/api/users?status=DEACTIVATED", TestContext.Current.CancellationToken);

        Assert.Contains((await IdentityAssertions.ReadAsync<JsonElement>(erased)).EnumerateArray(), u => u.GetProperty("id").GetGuid() == chief.Id);
        Assert.DoesNotContain((await IdentityAssertions.ReadAsync<JsonElement>(deactivated)).EnumerateArray(), u => u.GetProperty("id").GetGuid() == chief.Id);
    }

    [Fact]
    public async Task An_unknown_user_erases_nothing()
    {
        var result = await RunAsync(r => r.EraseAsync(new PersonalDataSubject.UserAccount(Guid.CreateVersion7()), Audit, TestContext.Current.CancellationToken));

        Assert.Null(result);
    }

    private static AuditRecord Audit(IReadOnlyDictionary<string, int> counts) =>
        new("PersonalDataErased", "PersonalDataRequest", Data: new { reference = "REQ-PRUEBA-4", counts });

    private async Task<T> RunAsync<T>(Func<PersonalDataRequests, Task<T>> run)
    {
        await using var scope = _registry.Services.CreateAsyncScope();
        return await run(scope.ServiceProvider.GetRequiredService<PersonalDataRequests>());
    }

    private async Task<SyntheticUser> ChiefWithNotificationsAsync(string email)
    {
        var chief = await _registry.Host.CreateUserAsync(email);
        await _registry.Host.AssignAsync(_registry.Own.Id, chief.Id);
        using (var session = await _registry.Host.SignInAsync(chief))
        {
            // Signing in records the user's own activity.
        }

        await using var scope = _registry.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        db.OptOuts.Add(new NotificationOptOut { UserId = chief.Id, Kind = NotificationKind.LicenseDigest, CreatedAt = DateTimeOffset.UtcNow });
        foreach (var topic in new[] { "digest:2031-01", "digest:2031-02" })
        {
            db.Deliveries.Add(new NotificationDelivery
            {
                Id = Guid.CreateVersion7(),
                UserId = chief.Id,
                Kind = NotificationKind.LicenseDigest,
                Template = "LicenseDigest",
                Topic = topic,
                Data = "{}",
                NextAttemptAt = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return chief;
    }

    private async Task InsertFailedSignInAsync(string normalisedEmail)
    {
        await using var scope = _registry.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        db.Add(new AuditEntry
        {
            Id = Guid.CreateVersion7(),
            OccurredAt = DateTimeOffset.UtcNow,
            Action = "SignInFailed",
            EntityType = "User",
            Data = JsonSerializer.Serialize(new { attemptedEmail = normalisedEmail, step = "password" }),
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<List<AuditEntry>> ActivityAsync(Guid userId)
    {
        await using var scope = _registry.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AuditDbContext>().Set<AuditEntry>().AsNoTracking()
            .Where(e => e.ActorUserId == userId).ToListAsync(TestContext.Current.CancellationToken);
    }
}
