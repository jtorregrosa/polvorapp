using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.IdentityAccess.Endpoints;
using PolvorApp.IdentityAccess.Security;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Identity;

/// <summary>Spec "Password reset by email".</summary>
public sealed partial class PasswordResetTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private const string NewPassword = "nueva-clave-sintetica-2026"; // gitleaks:allow (synthetic test value)
    private IdentityTestHost _host = null!;

    public async ValueTask InitializeAsync() => _host = await IdentityTestHost.StartAsync(postgres, mailpit);

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task An_active_user_gets_a_reset_link_in_their_language()
    {
        var user = await _host.CreateUserAsync("reset.valenciana@example.test", locale: "ca-ES-valencia");
        using var client = await _host.NewClientAsync();

        using var response = await client.PostAsync("/api/auth/password/forgot", new { email = user.Email });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var message = await mailpit.WaitForMessageAsync(user.Email);
        Assert.Equal("Restablir la teua contrasenya de PolvorApp", message.Subject);
        Assert.Contains("http://localhost:8080/password/reset?user=", message.Text, StringComparison.Ordinal);
        Assert.Contains(await _host.AuditEntriesAsync("PasswordResetRequested"), e => e.EntityId == user.Id.ToString());
    }

    [Fact]
    public async Task An_unknown_email_gets_the_same_answer_and_no_message()
    {
        using var client = await _host.NewClientAsync();
        const string unknown = "desconocida.reset@example.test";

        using var response = await client.PostAsync("/api/auth/password/forgot", new { email = unknown });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(0, response.Content.Headers.ContentLength ?? 0);
        await _host.DrainEmailAsync();
        Assert.Equal(0, await mailpit.CountMessagesAsync(unknown));
    }

    [Fact]
    public async Task Invited_and_deactivated_users_get_no_message()
    {
        var invited = await _host.CreateUserAsync("reset.invitada@example.test", withPassword: false, enrolled: false);
        var deactivated = await _host.CreateUserAsync("reset.desactivada@example.test", active: false);
        using var client = await _host.NewClientAsync();

        using var first = await client.PostAsync("/api/auth/password/forgot", new { email = invited.Email });
        using var second = await client.PostAsync("/api/auth/password/forgot", new { email = deactivated.Email });

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        await _host.DrainEmailAsync();
        Assert.Equal(0, await mailpit.CountMessagesAsync(invited.Email) + await mailpit.CountMessagesAsync(deactivated.Email));
    }

    [Fact]
    public async Task A_reset_ends_other_sessions_and_still_requires_the_second_factor()
    {
        var user = await _host.CreateUserAsync("reset.sesiones@example.test");
        using var otherSession = await _host.SignInAsync(user);
        var (userId, token) = await RequestResetAsync(user.Email);

        using var client = await _host.NewClientAsync();
        using var reset = await client.PostAsync("/api/auth/password/reset", new { userId, token, password = NewPassword });

        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        Assert.False(await IsSignedInAsync(otherSession));
        using var login = await client.PostAsync("/api/auth/login", new { email = user.Email, password = NewPassword });
        Assert.Equal(SignInStep.SecondFactor, (await ReadAsync<LoginResponse>(login)).Next);
        Assert.Contains(await _host.AuditEntriesAsync("PasswordReset"), e => e.EntityId == user.Id.ToString());
    }

    [Fact]
    public async Task A_reset_link_works_once()
    {
        var user = await _host.CreateUserAsync("reset.unico@example.test");
        var (userId, token) = await RequestResetAsync(user.Email);
        using var client = await _host.NewClientAsync();

        using var first = await client.PostAsync("/api/auth/password/reset", new { userId, token, password = NewPassword });
        using var second = await client.PostAsync("/api/auth/password/reset", new { userId, token, password = "otra-clave-sintetica-2026" }); // gitleaks:allow (synthetic test value)

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        await AssertProblemAsync(second, HttpStatusCode.Gone, "auth.invalidLink");
    }

    [Fact]
    public async Task A_weak_new_password_is_refused_with_the_failed_rules()
    {
        var user = await _host.CreateUserAsync("reset.debil@example.test");
        var (userId, token) = await RequestResetAsync(user.Email);
        using var client = await _host.NewClientAsync();

        using var response = await client.PostAsync("/api/auth/password/reset", new { userId, token, password = "corta" });

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "auth.invalidPassword");
        Assert.Contains("PasswordTooShort", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_forged_token_is_refused()
    {
        var user = await _host.CreateUserAsync("reset.falso@example.test");
        using var client = await _host.NewClientAsync();

        using var response = await client.PostAsync("/api/auth/password/reset", new { userId = user.Id, token = "forged", password = NewPassword });

        await AssertProblemAsync(response, HttpStatusCode.Gone, "auth.invalidLink");
    }

    [Fact]
    public void Reset_links_expire_after_one_hour_and_invitations_after_seven_days()
    {
        // Identity's token providers read the system clock, so the lifetimes are asserted directly.
        Assert.Equal(TimeSpan.FromHours(1), _host.Services.GetRequiredService<IOptions<DataProtectionTokenProviderOptions>>().Value.TokenLifespan);
        Assert.Equal(TimeSpan.FromDays(7), _host.Services.GetRequiredService<IOptions<InvitationTokenProviderOptions>>().Value.TokenLifespan);
    }

    private async Task<(Guid UserId, string Token)> RequestResetAsync(string email)
    {
        using var client = await _host.NewClientAsync();
        using var response = await client.PostAsync("/api/auth/password/forgot", new { email });
        var message = await mailpit.WaitForMessageAsync(email);
        var match = ResetLink().Match(message.Text);
        Assert.True(match.Success, "no reset link in the email");
        return (Guid.Parse(match.Groups["user"].Value), Uri.UnescapeDataString(match.Groups["token"].Value));
    }

    [GeneratedRegex(@"/password/reset\?user=(?<user>[0-9a-f-]+)&token=(?<token>\S+)")]
    private static partial Regex ResetLink();
}
