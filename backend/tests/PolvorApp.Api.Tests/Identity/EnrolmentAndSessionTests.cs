using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.IdentityAccess.Endpoints;
using PolvorApp.IdentityAccess.Security;
using PolvorApp.IdentityAccess.Users;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Identity;

/// <summary>Specs "Mandatory two-factor enrolment" and "Sessions".</summary>
[Collection(PostgresGroup.Name)]
public sealed class EnrolmentAndSessionTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private IdentityTestHost _host = null!;

    public async ValueTask InitializeAsync() => _host = await IdentityTestHost.StartAsync(postgres, mailpit);

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task Enrolment_needs_a_verified_password_first()
    {
        using var client = await _host.NewClientAsync();

        using var response = await client.GetAsync("/api/auth/enrolment", TestContext.Current.CancellationToken);

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "auth.stepExpired");
    }

    [Fact]
    public async Task An_enrolled_user_cannot_replace_the_authenticator_through_enrolment()
    {
        var user = await _host.CreateUserAsync("ya-inscrita@example.test");
        using var client = await _host.NewClientAsync();
        using var login = await client.PostAsync("/api/auth/login", new { email = user.Email, password = IdentityTestHost.Password });

        using var response = await client.GetAsync("/api/auth/enrolment", TestContext.Current.CancellationToken);

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "auth.stepExpired");
    }

    [Fact]
    public async Task A_wrong_enrolment_code_does_not_enable_two_factor()
    {
        var (client, _, _) = await StartEnrolmentAsync("inscripcion-erronea@example.test");

        using var response = await client.PostAsync("/api/auth/enrolment", new { code = "000000" });

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "auth.invalidCode");
        Assert.False(await IsSignedInAsync(client));
    }

    [Fact]
    public async Task Enrolment_shows_ten_recovery_codes_once_and_signs_in()
    {
        var (client, enrolment, user) = await StartEnrolmentAsync("inscripcion@example.test");
        Assert.StartsWith("otpauth://totp/PolvorApp%3Ainscripcion%40example.test?secret=", enrolment.AuthenticatorUri, StringComparison.Ordinal);

        _host.Time.Advance(Totp.Step);
        var key = enrolment.SharedKey.Replace(" ", string.Empty, StringComparison.Ordinal);
        var code = Totp.Compute(Totp.DecodeBase32(key), Totp.StepAt(_host.Time.GetUtcNow()));
        using var response = await client.PostAsync("/api/auth/enrolment", new { code });

        var codes = (await ReadAsync<RecoveryCodesResponse>(response)).RecoveryCodes;
        Assert.Equal(10, codes.Count);
        Assert.Equal(10, codes.Distinct().Count());
        Assert.True(await IsSignedInAsync(client));
        var enrolled = Assert.Single(await _host.AuditEntriesAsync("TwoFactorEnrolled"), e => e.EntityId == user.Id.ToString());
        Assert.Equal(user.Id, enrolled.ActorUserId);

        using var again = await client.GetAsync("/api/auth/enrolment", TestContext.Current.CancellationToken);
        await AssertProblemAsync(again, HttpStatusCode.Unauthorized, "auth.stepExpired");
    }

    [Fact]
    public async Task Signing_out_ends_the_session()
    {
        var user = await _host.CreateUserAsync("salir@example.test");
        using var client = await _host.SignInAsync(user);

        using var response = await client.PostAsync("/api/auth/logout", new { });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(await IsSignedInAsync(client));
    }

    [Fact]
    public async Task The_session_expires_after_sixty_idle_minutes()
    {
        var user = await _host.CreateUserAsync("inactiva@example.test");
        using var client = await _host.SignInAsync(user);

        _host.Time.Advance(TimeSpan.FromMinutes(59));
        Assert.True(await IsSignedInAsync(client));
        _host.Time.Advance(TimeSpan.FromMinutes(61));
        Assert.False(await IsSignedInAsync(client));
    }

    [Fact]
    public async Task The_session_ends_twelve_hours_after_sign_in_even_when_active()
    {
        var user = await _host.CreateUserAsync("jornada@example.test");
        using var client = await _host.SignInAsync(user);

        for (var elapsed = TimeSpan.Zero; elapsed < TimeSpan.FromHours(11.5); elapsed += TimeSpan.FromMinutes(30))
        {
            _host.Time.Advance(TimeSpan.FromMinutes(30));
            Assert.True(await IsSignedInAsync(client), $"signed out early after {elapsed + TimeSpan.FromMinutes(30)}");
        }

        _host.Time.Advance(TimeSpan.FromMinutes(45));
        Assert.False(await IsSignedInAsync(client));
    }

    [Fact]
    public async Task A_security_stamp_change_ends_the_session_on_the_next_request()
    {
        var user = await _host.CreateUserAsync("sello-sesion@example.test");
        using var client = await _host.SignInAsync(user);

        await using (var scope = _host.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            await users.UpdateSecurityStampAsync((await users.FindByIdAsync(user.Id.ToString()))!);
        }

        Assert.False(await IsSignedInAsync(client));
    }

    [Fact]
    public async Task The_session_cookie_is_http_only_and_same_site_strict()
    {
        var user = await _host.CreateUserAsync("galleta@example.test");
        using var client = await _host.NewClientAsync();
        using var login = await client.PostAsync("/api/auth/login", new { email = user.Email, password = IdentityTestHost.Password });

        using var response = await client.PostAsync("/api/auth/login/second-factor", new { code = _host.NextCode(user), rememberDevice = false });

        var session = Assert.Single(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("polvorapp.session=", StringComparison.Ordinal));
        Assert.Contains("httponly", session, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", session, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<(HttpClient Client, EnrolmentResponse Enrolment, SyntheticUser User)> StartEnrolmentAsync(string email)
    {
        var user = await _host.CreateUserAsync(email, enrolled: false);
        var client = await _host.NewClientAsync();
        using var login = await client.PostAsync("/api/auth/login", new { email = user.Email, password = IdentityTestHost.Password });
        Assert.Equal(SignInStep.Enrol, (await ReadAsync<LoginResponse>(login)).Next);
        using var enrolment = await client.GetAsync("/api/auth/enrolment", TestContext.Current.CancellationToken);
        return (client, await ReadAsync<EnrolmentResponse>(enrolment), user);
    }
}
