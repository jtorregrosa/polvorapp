using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.IdentityAccess.Emails;
using PolvorApp.IdentityAccess.Users;
using PolvorApp.SharedKernel.Email;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Identity;

/// <summary>Findings of the group 5 reviews on password reset, pending steps and email texts.</summary>
[Collection(PostgresGroup.Name)]
public sealed partial class PasswordResetHardeningTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private IdentityTestHost _host = null!;

    public async ValueTask InitializeAsync() => _host = await IdentityTestHost.StartAsync(postgres, mailpit);

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task A_user_without_two_factor_gets_no_reset_email()
    {
        var user = await _host.CreateUserAsync("sin.2fa.reset@example.test", enrolled: false);
        using var client = await _host.NewClientAsync();

        using var response = await client.PostAsync("/api/auth/password/forgot", new { email = user.Email });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        await _host.DrainEmailAsync();
        Assert.Equal(0, await mailpit.CountMessagesAsync(user.Email));
    }

    [Fact]
    public async Task One_account_gets_at_most_one_reset_email_every_five_minutes()
    {
        var user = await _host.CreateUserAsync("una.vez@example.test");
        using var client = await _host.NewClientAsync();

        using (var first = await client.PostAsync("/api/auth/password/forgot", new { email = user.Email }))
        using (var second = await client.PostAsync("/api/auth/password/forgot", new { email = user.Email }))
        {
            Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        }

        await _host.DrainEmailAsync();
        await mailpit.WaitForMessageAsync(user.Email);
        Assert.Equal(1, await mailpit.CountMessagesAsync(user.Email));
    }

    [Fact]
    public async Task A_password_reset_cancels_a_pending_second_step()
    {
        var user = await _host.CreateUserAsync("paso.pendiente@example.test");
        using var attacker = await _host.NewClientAsync();
        using (var login = await attacker.PostAsync("/api/auth/login", new { email = user.Email, password = IdentityTestHost.Password }))
        {
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        }

        using var owner = await _host.NewClientAsync();
        using (var forgot = await owner.PostAsync("/api/auth/password/forgot", new { email = user.Email }))
        {
            Assert.Equal(HttpStatusCode.Accepted, forgot.StatusCode);
        }

        var match = ResetLink().Match((await mailpit.WaitForMessageAsync(user.Email)).Text);
        using (var reset = await owner.PostAsync("/api/auth/password/reset", new
        {
            userId = Guid.Parse(match.Groups["user"].Value),
            token = Uri.UnescapeDataString(match.Groups["token"].Value),
            password = "nueva-clave-sintetica-2026", // gitleaks:allow (synthetic test value)
        }))
        {
            Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        }

        using var stale = await attacker.PostAsync("/api/auth/login/second-factor", new { code = _host.NextCode(user), rememberDevice = false });
        await AssertProblemAsync(stale, HttpStatusCode.Unauthorized, "auth.stepExpired");
    }

    [Theory]
    [InlineData("Ana\nFalso enlace: https://evil.example")]
    [InlineData("Nombre\u202Eodilav")]
    public async Task Names_with_line_breaks_or_bidi_controls_are_refused(string name)
    {
        using var admin = await _host.SignInAsync(await _host.CreateUserAsync($"admin.nombres.{name.Length}@example.test", UserRole.Admin));

        using var response = await admin.PostAsync("/api/users", new { email = $"nombre.{name.Length}@example.test", name, role = "FIRING_CHIEF", locale = "es-ES" });

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation");
    }

    [Theory]
    [InlineData("es-ES")]
    [InlineData("ca-ES-valencia")]
    [InlineData("en")]
    public async Task Every_email_text_exists_in_every_language(string locale)
    {
        var sent = new List<EmailMessage>();
        await using var scope = _host.Services.CreateAsyncScope();
        var emails = ActivatorUtilities.CreateInstance<IdentityEmails>(scope.ServiceProvider, new CapturingSender(sent), new CapturingOutbox(sent));
        var user = new User
        {
            Email = "plantillas@example.test",
            Name = "Persona {0} <b>",
            Role = UserRole.FiringChief,
            Locale = locale,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        await emails.SendInvitationAsync(user, "token", TestContext.Current.CancellationToken);
        emails.QueuePasswordReset(user, "token");

        Assert.Equal(2, sent.Count);
        Assert.All(sent, message =>
        {
            Assert.DoesNotContain(".Subject", message.Subject, StringComparison.Ordinal);
            Assert.Contains("token=token", message.TextBody, StringComparison.Ordinal);
            Assert.Contains("Persona {0} <b>", message.TextBody, StringComparison.Ordinal);
            Assert.Contains("Persona {0} &lt;b&gt;", message.HtmlBody, StringComparison.Ordinal);
        });
        Assert.Contains(sent, m => m.TextBody.Contains(" 7 ", StringComparison.Ordinal));
        Assert.Contains(sent, m => m.TextBody.Contains(60.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal));
    }

    private sealed class CapturingSender(List<EmailMessage> sent) : IEmailSender
    {
        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            sent.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class CapturingOutbox(List<EmailMessage> sent) : IEmailOutbox
    {
        public void Enqueue(EmailMessage message) => sent.Add(message);
    }

    [GeneratedRegex(@"/password/reset\?user=(?<user>[0-9a-f-]+)&token=(?<token>\S+)")]
    private static partial Regex ResetLink();
}
