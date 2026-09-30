using System.Net;
using System.Text.RegularExpressions;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.IdentityAccess.Endpoints;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Identity;

/// <summary>Spec "Invitation-only accounts".</summary>
[Collection(PostgresGroup.Name)]
public sealed partial class InvitationTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private IdentityTestHost _host = null!;
    private HttpClient _admin = null!;

    public async ValueTask InitializeAsync()
    {
        _host = await IdentityTestHost.StartAsync(postgres, mailpit);
        _admin = await _host.SignInAsync(await _host.CreateUserAsync("admin.invita@example.test", UserRole.Admin));
    }

    public async ValueTask DisposeAsync()
    {
        _admin.Dispose();
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task An_admin_invites_a_firing_chief_in_their_language()
    {
        using var response = await _admin.PostAsync("/api/users", new
        {
            email = "jefe.sintetico@example.test",
            name = "Jefe Sintético",
            role = "FIRING_CHIEF",
            locale = "ca-ES-valencia",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var user = await ReadAsync<UserResponse>(response);
        Assert.Equal(PolvorApp.IdentityAccess.Users.UserStatus.Invited, user.Status);
        var message = await mailpit.WaitForMessageAsync("jefe.sintetico@example.test");
        Assert.Equal("Invitació a PolvorApp", message.Subject);
        Assert.Contains("/invitations/accept?user=", message.Text, StringComparison.Ordinal);
        Assert.Contains(await _host.AuditEntriesAsync("UserInvited"), e => e.EntityId == user.Id.ToString());
    }

    [Fact]
    public async Task An_email_that_exists_in_any_case_is_a_conflict()
    {
        await _host.CreateUserAsync("Nuria.Ejemplo@example.test");

        using var response = await _admin.PostAsync("/api/users", new { email = "nuria.ejemplo@example.test", name = "Nuria", role = "FIRING_CHIEF", locale = "es-ES" });

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "users.emailTaken");
    }

    [Theory]
    [InlineData("role", "SUPERVISOR")]
    [InlineData("locale", "fr-FR")]
    [InlineData("email", "no-es-un-email")]
    [InlineData("name", "")]
    public async Task Invalid_fields_are_named(string field, string value)
    {
        var body = new Dictionary<string, string>
        {
            ["email"] = $"campo.{field}@example.test",
            ["name"] = "Persona Sintética",
            ["role"] = "FIRING_CHIEF",
            ["locale"] = "es-ES",
            [field] = value,
        };

        using var response = await _admin.PostAsync("/api/users", body);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation");
        Assert.Contains($"\"{field}\"", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_firing_chief_cannot_invite()
    {
        using var chief = await _host.SignInAsync(await _host.CreateUserAsync("jefe.no.invita@example.test"));

        using var response = await chief.PostAsync("/api/users", new { email = "otra@example.test", name = "Otra", role = "FIRING_CHIEF", locale = "es-ES" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task The_invitee_sets_a_password_enrols_and_the_link_stops_working()
    {
        var (userId, token) = await InviteAsync("invitada.completa@example.test");
        using var client = await _host.NewClientAsync();

        using var validate = await client.GetAsync($"/api/auth/invitations/validate?user={userId}&token={Uri.EscapeDataString(token)}", TestContext.Current.CancellationToken);
        Assert.Equal("invitada.completa@example.test", (await ReadAsync<InvitationResponse>(validate)).Email);

        using var accept = await client.PostAsync("/api/auth/invitations/accept", new { userId, token, password = IdentityTestHost.Password });
        Assert.Equal(SignInStep.Enrol, (await ReadAsync<LoginResponse>(accept)).Next);
        using var enrolment = await client.GetAsync("/api/auth/enrolment", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, enrolment.StatusCode);

        using var again = await (await _host.NewClientAsync()).PostAsync("/api/auth/invitations/accept", new { userId, token, password = "otra-clave-sintetica" });
        await AssertProblemAsync(again, HttpStatusCode.Gone, "auth.invalidLink");
    }

    [Fact]
    public async Task A_weak_password_keeps_the_invitation_open()
    {
        var (userId, token) = await InviteAsync("invitada.debil@example.test");
        using var client = await _host.NewClientAsync();

        using var weak = await client.PostAsync("/api/auth/invitations/accept", new { userId, token, password = "corta" });
        using var good = await client.PostAsync("/api/auth/invitations/accept", new { userId, token, password = IdentityTestHost.Password });

        await AssertProblemAsync(weak, HttpStatusCode.BadRequest, "auth.invalidPassword");
        Assert.Equal(HttpStatusCode.OK, good.StatusCode);
    }

    [Fact]
    public async Task Resending_replaces_the_previous_link()
    {
        var (userId, first) = await InviteAsync("invitada.reenvio@example.test");

        using var resend = await _admin.PostAsync($"/api/users/{userId}/invitation", new { });
        Assert.Equal(HttpStatusCode.OK, resend.StatusCode);
        var second = await LinkAsync("invitada.reenvio@example.test", token => token != first);

        using var client = await _host.NewClientAsync();
        using var old = await client.PostAsync("/api/auth/invitations/accept", new { userId, token = first, password = IdentityTestHost.Password });
        using var current = await client.PostAsync("/api/auth/invitations/accept", new { userId, token = second.Token, password = IdentityTestHost.Password });
        await AssertProblemAsync(old, HttpStatusCode.Gone, "auth.invalidLink");
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);
    }

    [Fact]
    public async Task Resending_to_an_active_user_is_a_conflict()
    {
        var active = await _host.CreateUserAsync("ya.activa@example.test");

        using var response = await _admin.PostAsync($"/api/users/{active.Id}/invitation", new { });

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "users.notInvited");
    }

    [Fact]
    public async Task Deactivating_an_invited_user_invalidates_the_link()
    {
        var (userId, token) = await InviteAsync("invitada.desactivada@example.test");
        using var deactivate = await _admin.PostAsync($"/api/users/{userId}/deactivate", new { });

        using var client = await _host.NewClientAsync();
        using var accept = await client.PostAsync("/api/auth/invitations/accept", new { userId, token, password = IdentityTestHost.Password });

        await AssertProblemAsync(accept, HttpStatusCode.Gone, "auth.invalidLink");
    }

    [Fact]
    public async Task A_forged_link_is_no_longer_valid()
    {
        var (userId, _) = await InviteAsync("invitada.falsa@example.test");
        using var client = await _host.NewClientAsync();

        using var response = await client.GetAsync($"/api/auth/invitations/validate?user={userId}&token=forged", TestContext.Current.CancellationToken);

        await AssertProblemAsync(response, HttpStatusCode.Gone, "auth.invalidLink");
    }

    private async Task<(Guid UserId, string Token)> InviteAsync(string email)
    {
        using var response = await _admin.PostAsync("/api/users", new { email, name = "Persona Invitada", role = "FIRING_CHIEF", locale = "es-ES" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await LinkAsync(email);
    }

    private async Task<(Guid UserId, string Token)> LinkAsync(string email, Func<string, bool>? accept = null)
    {
        var message = await mailpit.WaitForMessageAsync(email, m =>
        {
            var link = InvitationLink().Match(m.Text);
            return link.Success && (accept is null || accept(Uri.UnescapeDataString(link.Groups["token"].Value)));
        });
        var match = InvitationLink().Match(message.Text);
        return (Guid.Parse(match.Groups["user"].Value), Uri.UnescapeDataString(match.Groups["token"].Value));
    }

    [GeneratedRegex(@"/invitations/accept\?user=(?<user>[0-9a-f-]+)&token=(?<token>\S+)")]
    private static partial Regex InvitationLink();
}

/// <summary>Spec platform "Transactional email", scenario "SMTP server unavailable".</summary>
[Collection(PostgresGroup.Name)]
public sealed class InvitationEmailFailureTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private IdentityTestHost _host = null!;

    public async ValueTask InitializeAsync() => _host = await IdentityTestHost.StartAsync(
        postgres, mailpit, new Dictionary<string, string?> { ["Email:SmtpHost"] = "127.0.0.1", ["Email:SmtpPort"] = RejectingSmtpServer.ClosedPort().ToString(System.Globalization.CultureInfo.InvariantCulture) });

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task An_unsent_invitation_keeps_the_user_and_reports_it()
    {
        using var admin = await _host.SignInAsync(await _host.CreateUserAsync("admin.sin.correo@example.test", UserRole.Admin));

        using var response = await admin.PostAsync("/api/users", new { email = "sin.correo@example.test", name = "Sin Correo", role = "FIRING_CHIEF", locale = "es-ES" });

        await AssertProblemAsync(response, HttpStatusCode.BadGateway, "email.sendFailed");
        using var list = await admin.GetAsync("/api/users?status=INVITED", TestContext.Current.CancellationToken);
        Assert.Contains(await ReadAsync<List<UserResponse>>(list), u => u.Email == "sin.correo@example.test");
    }
}
