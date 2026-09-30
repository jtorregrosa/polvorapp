using System.Net;
using System.Net.Http.Json;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.IdentityAccess.Endpoints;
using PolvorApp.IdentityAccess.Users;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Identity;

/// <summary>Spec "User management by Admins".</summary>
[Collection(PostgresGroup.Name)]
public sealed class UserManagementTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private IdentityTestHost _host = null!;
    private SyntheticUser _adminUser = null!;
    private HttpClient _admin = null!;

    public async ValueTask InitializeAsync()
    {
        _host = await IdentityTestHost.StartAsync(postgres, mailpit);
        _adminUser = await _host.CreateUserAsync("admin.gestion@example.test", UserRole.Admin);
        _admin = await _host.SignInAsync(_adminUser);
    }

    public async ValueTask DisposeAsync()
    {
        _admin.Dispose();
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task An_admin_lists_users_with_status_two_factor_and_last_sign_in()
    {
        await _host.CreateUserAsync("lista.invitada@example.test", withPassword: false, enrolled: false);
        await _host.CreateUserAsync("lista.desactivada@example.test", active: false);

        using var response = await _admin.GetAsync("/api/users", TestContext.Current.CancellationToken);

        var users = await ReadAsync<List<UserResponse>>(response);
        Assert.Equal(UserStatus.Invited, users.Single(u => u.Email == "lista.invitada@example.test").Status);
        Assert.Equal(UserStatus.Deactivated, users.Single(u => u.Email == "lista.desactivada@example.test").Status);
        var admin = users.Single(u => u.Email == _adminUser.Email);
        Assert.Equal(UserStatus.Active, admin.Status);
        Assert.True(admin.TwoFactorEnabled);
        Assert.NotNull(admin.LastSignInAt);
    }

    [Theory]
    [InlineData("?role=ADMIN", "admin.gestion@example.test", "filtro.jefe@example.test")]
    [InlineData("?status=INVITED", "filtro.invitada@example.test", "admin.gestion@example.test")]
    [InlineData("?role=FIRING_CHIEF&status=ACTIVE", "filtro.jefe@example.test", "filtro.invitada@example.test")]
    public async Task Users_can_be_filtered_by_role_and_status(string query, string included, string excluded)
    {
        await _host.CreateUserAsync("filtro.jefe@example.test");
        await _host.CreateUserAsync("filtro.invitada@example.test", withPassword: false, enrolled: false);

        using var response = await _admin.GetAsync($"/api/users{query}", TestContext.Current.CancellationToken);

        var emails = (await ReadAsync<List<UserResponse>>(response)).Select(u => u.Email).ToList();
        Assert.Contains(included, emails);
        Assert.DoesNotContain(excluded, emails);
    }

    [Fact]
    public async Task A_firing_chief_gets_403_from_every_user_endpoint()
    {
        var target = await _host.CreateUserAsync("objetivo@example.test");
        using var chief = await _host.SignInAsync(await _host.CreateUserAsync("jefe.prohibido@example.test"));

        var responses = new[]
        {
            await chief.GetAsync("/api/users", TestContext.Current.CancellationToken),
            await chief.GetAsync($"/api/users/{target.Id}", TestContext.Current.CancellationToken),
            await chief.PostAsync($"/api/users/{target.Id}/deactivate", new { }),
            await chief.PostAsync($"/api/users/{target.Id}/two-factor/reset", new { }),
        };

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode));
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task Deactivation_ends_the_session_and_blocks_sign_in_and_is_audited()
    {
        var user = await _host.CreateUserAsync("desactivar@example.test");
        using var session = await _host.SignInAsync(user);

        using var response = await _admin.PostAsync($"/api/users/{user.Id}/deactivate", new { });

        Assert.Equal(UserStatus.Deactivated, (await ReadAsync<UserResponse>(response)).Status);
        Assert.False(await IsSignedInAsync(session));
        using var login = await (await _host.NewClientAsync()).PostAsync("/api/auth/login", new { email = user.Email, password = IdentityTestHost.Password });
        await AssertProblemAsync(login, HttpStatusCode.Unauthorized, "auth.invalidCredentials");
        var entry = Assert.Single(await _host.AuditEntriesAsync("UserDeactivated"), e => e.EntityId == user.Id.ToString());
        Assert.Equal(_adminUser.Id, entry.ActorUserId);
    }

    [Fact]
    public async Task A_reactivated_user_signs_in_again()
    {
        var user = await _host.CreateUserAsync("reactivar@example.test", active: false);

        using var response = await _admin.PostAsync($"/api/users/{user.Id}/reactivate", new { });

        Assert.Equal(UserStatus.Active, (await ReadAsync<UserResponse>(response)).Status);
        using var session = await _host.SignInAsync(user);
        Assert.True(await IsSignedInAsync(session));
    }

    [Fact]
    public async Task The_last_active_admin_cannot_deactivate_or_demote_themselves()
    {
        using var deactivate = await _admin.PostAsync($"/api/users/{_adminUser.Id}/deactivate", new { });
        using var demote = await _admin.PutAsJsonAsync(
            $"/api/users/{_adminUser.Id}", new { name = "Admin", role = "FIRING_CHIEF", locale = "es-ES" }, TestContext.Current.CancellationToken);

        await AssertProblemAsync(deactivate, HttpStatusCode.Conflict, "users.lastAdmin");
        await AssertProblemAsync(demote, HttpStatusCode.Conflict, "users.lastAdmin");
    }

    [Fact]
    public async Task Two_admins_demoting_each_other_at_once_leave_one_admin()
    {
        var other = await _host.CreateUserAsync("admin.otra@example.test", UserRole.Admin);
        using var otherClient = await _host.SignInAsync(other);

        var results = await Task.WhenAll(
            _admin.PutAsJsonAsync($"/api/users/{other.Id}", new { name = "Otra", role = "FIRING_CHIEF", locale = "es-ES" }, TestContext.Current.CancellationToken),
            otherClient.PutAsJsonAsync($"/api/users/{_adminUser.Id}", new { name = "Admin", role = "FIRING_CHIEF", locale = "es-ES" }, TestContext.Current.CancellationToken));

        var statuses = results.Select(r => r.StatusCode).Order().ToList();
        Assert.Contains(HttpStatusCode.OK, statuses);
        Assert.DoesNotContain(statuses, s => s is not (HttpStatusCode.OK or HttpStatusCode.Conflict or HttpStatusCode.Forbidden));
        using var list = await (await _host.SignInAsync(await _host.CreateUserAsync("admin.verifica@example.test", UserRole.Admin))).GetAsync("/api/users?role=ADMIN&status=ACTIVE", TestContext.Current.CancellationToken);
        var admins = (await ReadAsync<List<UserResponse>>(list)).Select(u => u.Email).ToList();
        Assert.True(admins.Contains(_adminUser.Email) || admins.Contains(other.Email), "both original admins were demoted");
        foreach (var response in results)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task A_role_change_applies_on_the_next_request_and_is_audited()
    {
        var other = await _host.CreateUserAsync("admin.degradada@example.test", UserRole.Admin);
        using var otherSession = await _host.SignInAsync(other);

        using var response = await _admin.PutAsJsonAsync(
            $"/api/users/{other.Id}", new { name = "Degradada", role = "FIRING_CHIEF", locale = "en" }, TestContext.Current.CancellationToken);

        Assert.Equal(UserRole.FiringChief, (await ReadAsync<UserResponse>(response)).Role);
        using var forbidden = await otherSession.GetAsync("/api/users", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.True(await IsSignedInAsync(otherSession));
        var entry = Assert.Single(await _host.AuditEntriesAsync("UserUpdated"), e => e.EntityId == other.Id.ToString());
        Assert.Contains("\"ADMIN\"", entry.Data, StringComparison.Ordinal);
        Assert.Contains("\"FIRING_CHIEF\"", entry.Data, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_two_factor_reset_forces_enrolment_and_ends_sessions()
    {
        var user = await _host.CreateUserAsync("perdio.movil@example.test");
        using var session = await _host.SignInAsync(user);

        using var response = await _admin.PostAsync($"/api/users/{user.Id}/two-factor/reset", new { });

        Assert.False((await ReadAsync<UserResponse>(response)).TwoFactorEnabled);
        Assert.False(await IsSignedInAsync(session));
        using var login = await (await _host.NewClientAsync()).PostAsync("/api/auth/login", new { email = user.Email, password = IdentityTestHost.Password });
        Assert.Equal(SignInStep.Enrol, (await ReadAsync<LoginResponse>(login)).Next);
        Assert.Contains(await _host.AuditEntriesAsync("TwoFactorReset"), e => e.EntityId == user.Id.ToString());
    }

    [Fact]
    public async Task Resetting_two_factor_of_a_user_without_it_is_a_conflict()
    {
        var user = await _host.CreateUserAsync("sin.2fa@example.test", enrolled: false);

        using var response = await _admin.PostAsync($"/api/users/{user.Id}/two-factor/reset", new { });

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "users.notEnrolled");
    }

    [Fact]
    public async Task An_unknown_user_is_not_found()
    {
        using var response = await _admin.GetAsync($"/api/users/{Guid.CreateVersion7()}", TestContext.Current.CancellationToken);

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "users.notFound");
    }
}
