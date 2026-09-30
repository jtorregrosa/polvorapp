using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.IdentityAccess.Endpoints;
using PolvorApp.IdentityAccess.Users;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Identity;

/// <summary>Specs "Sign-in with two-factor authentication" and "Remembered devices".</summary>
[Collection(PostgresGroup.Name)]
public sealed class SignInTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private IdentityTestHost _host = null!;

    public async ValueTask InitializeAsync() => _host = await IdentityTestHost.StartAsync(postgres, mailpit);

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task Wrong_password_unknown_email_and_deactivated_account_look_the_same()
    {
        var active = await _host.CreateUserAsync("activa@example.test");
        var deactivated = await _host.CreateUserAsync("desactivada@example.test", active: false);
        using var client = await _host.NewClientAsync();

        using var wrongPassword = await client.PostAsync("/api/auth/login", new { email = active.Email, password = "incorrecta-1234" }); // gitleaks:allow (synthetic test value)
        using var unknown = await client.PostAsync("/api/auth/login", new { email = "nadie@example.test", password = IdentityTestHost.Password });
        using var inactive = await client.PostAsync("/api/auth/login", new { email = deactivated.Email, password = IdentityTestHost.Password });

        foreach (var response in new[] { wrongPassword, unknown, inactive })
        {
            await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "auth.invalidCredentials");
        }

        Assert.False(await IsSignedInAsync(client));
    }

    [Fact]
    public async Task A_failed_attempt_is_audited_without_the_password()
    {
        using var client = await _host.NewClientAsync();

        using var response = await client.PostAsync("/api/auth/login", new { email = "Intento.Fallido@Example.test", password = "secreto-no-registrado" });

        var entry = Assert.Single(await _host.AuditEntriesAsync("SignInFailed"), e => e.Data!.Contains("INTENTO.FALLIDO@EXAMPLE.TEST", StringComparison.Ordinal));
        Assert.Null(entry.ActorUserId);
        Assert.DoesNotContain("secreto-no-registrado", entry.Data, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_user_without_authenticator_must_enrol_and_is_not_signed_in_yet()
    {
        var user = await _host.CreateUserAsync("sin-autenticador@example.test", enrolled: false);
        using var client = await _host.NewClientAsync();

        using var response = await client.PostAsync("/api/auth/login", new { email = user.Email, password = IdentityTestHost.Password });

        Assert.Equal(SignInStep.Enrol, (await ReadAsync<LoginResponse>(response)).Next);
        Assert.False(await IsSignedInAsync(client));
    }

    [Fact]
    public async Task The_password_alone_never_signs_in_an_enrolled_user()
    {
        var user = await _host.CreateUserAsync("dos-pasos@example.test");
        using var client = await _host.NewClientAsync();

        using var response = await client.PostAsync("/api/auth/login", new { email = user.Email, password = IdentityTestHost.Password });

        Assert.Equal(SignInStep.SecondFactor, (await ReadAsync<LoginResponse>(response)).Next);
        Assert.False(await IsSignedInAsync(client));
    }

    [Fact]
    public async Task A_valid_code_signs_in_records_the_time_and_audits_it()
    {
        var user = await _host.CreateUserAsync("codigo-valido@example.test");

        using var client = await _host.SignInAsync(user);

        Assert.True(await IsSignedInAsync(client));
        Assert.Contains(await _host.AuditEntriesAsync("SignedIn"), e => e.EntityId == user.Id.ToString() && e.ActorUserId == user.Id);
        await using var scope = _host.Services.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<UserManager<User>>().FindByIdAsync(user.Id.ToString());
        Assert.Equal(_host.Time.GetUtcNow(), stored!.LastSignInAt!.Value, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task A_code_cannot_be_used_twice()
    {
        var user = await _host.CreateUserAsync("sin-repeticion@example.test");
        var code = _host.NextCode(user);
        using var first = await PasswordStepAsync(user);
        using var second = await PasswordStepAsync(user);

        using var accepted = await first.PostAsync("/api/auth/login/second-factor", new { code, rememberDevice = false });
        using var replayed = await second.PostAsync("/api/auth/login/second-factor", new { code, rememberDevice = false });

        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        await AssertProblemAsync(replayed, HttpStatusCode.Unauthorized, "auth.invalidCode");
    }

    [Fact]
    public async Task Five_failures_lock_the_account_for_fifteen_minutes()
    {
        var user = await _host.CreateUserAsync("bloqueo@example.test");
        using var client = await _host.NewClientAsync();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var failed = await client.PostAsync("/api/auth/login", new { email = user.Email, password = "incorrecta-1234" }); // gitleaks:allow (synthetic test value)
        }

        using var locked = await client.PostAsync("/api/auth/login", new { email = user.Email, password = IdentityTestHost.Password });
        await AssertProblemAsync(locked, HttpStatusCode.Unauthorized, "auth.invalidCredentials");
        Assert.Contains(await _host.AuditEntriesAsync("LockedOut"), e => e.EntityId == user.Id.ToString());

        // Identity's lockout uses the system clock: check the end time, then let it pass.
        await using (var scope = _host.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var stored = (await users.FindByIdAsync(user.Id.ToString()))!;
            Assert.Equal(DateTimeOffset.UtcNow.AddMinutes(15), stored.LockoutEnd!.Value, TimeSpan.FromMinutes(1));
            await users.SetLockoutEndDateAsync(stored, DateTimeOffset.UtcNow.AddSeconds(-1));
        }

        using var unlocked = await client.PostAsync("/api/auth/login", new { email = user.Email, password = IdentityTestHost.Password });
        Assert.Equal(SignInStep.SecondFactor, (await ReadAsync<LoginResponse>(unlocked)).Next);
    }

    [Fact]
    public async Task Wrong_codes_count_towards_the_lockout()
    {
        var user = await _host.CreateUserAsync("bloqueo-codigo@example.test");
        using var client = await PasswordStepAsync(user);

        for (var attempt = 0; attempt < 4; attempt++)
        {
            using var wrong = await client.PostAsync("/api/auth/login/second-factor", new { code = "000000", rememberDevice = false });
            await AssertProblemAsync(wrong, HttpStatusCode.Unauthorized, "auth.invalidCode");
        }

        using var fifth = await client.PostAsync("/api/auth/login/second-factor", new { code = "000000", rememberDevice = false });
        await AssertProblemAsync(fifth, HttpStatusCode.Unauthorized, "auth.lockedOut");
        using var correct = await client.PostAsync("/api/auth/login/second-factor", new { code = _host.NextCode(user), rememberDevice = false });
        await AssertProblemAsync(correct, HttpStatusCode.Unauthorized, "auth.lockedOut");
    }

    [Fact]
    public async Task Code_failures_keep_counting_when_the_password_is_entered_again()
    {
        var user = await _host.CreateUserAsync("reintento-codigo@example.test");

        // Someone with the password cannot reset the count by starting over before each guess.
        for (var attempt = 0; attempt < 4; attempt++)
        {
            using var client = await PasswordStepAsync(user);
            using var wrong = await client.PostAsync("/api/auth/login/second-factor", new { code = "000000", rememberDevice = false });
            await AssertProblemAsync(wrong, HttpStatusCode.Unauthorized, "auth.invalidCode");
        }

        using var last = await PasswordStepAsync(user);
        using var fifth = await last.PostAsync("/api/auth/login/second-factor", new { code = "000000", rememberDevice = false });
        await AssertProblemAsync(fifth, HttpStatusCode.Unauthorized, "auth.lockedOut");
        using var correct = await last.PostAsync("/api/auth/login/second-factor", new { code = _host.NextCode(user), rememberDevice = false });
        await AssertProblemAsync(correct, HttpStatusCode.Unauthorized, "auth.lockedOut");
    }

    [Fact]
    public async Task A_complete_sign_in_starts_the_failure_count_again()
    {
        var user = await _host.CreateUserAsync("cuenta-reiniciada@example.test");
        await FailCodesAsync(user, 4);
        using (var signedIn = await _host.SignInAsync(user))
        {
            Assert.True(await IsSignedInAsync(signedIn));
        }

        await FailCodesAsync(user, 4);

        using var again = await _host.SignInAsync(user);
        Assert.True(await IsSignedInAsync(again));
    }

    [Fact]
    public async Task A_recovery_code_signs_in_once_and_reports_how_many_remain()
    {
        var user = await _host.CreateUserAsync("recuperacion@example.test");
        var codes = await RecoveryCodesAsync(user);

        using var client = await PasswordStepAsync(user);
        using var used = await client.PostAsync("/api/auth/login/recovery-code", new { code = codes[0] });
        Assert.Equal(9, (await ReadAsync<RecoveryCodeResponse>(used)).RecoveryCodesLeft);
        Assert.True(await IsSignedInAsync(client));
        Assert.Contains(await _host.AuditEntriesAsync("RecoveryCodeUsed"), e => e.EntityId == user.Id.ToString());

        using var again = await PasswordStepAsync(user);
        using var reused = await again.PostAsync("/api/auth/login/recovery-code", new { code = codes[0] });
        await AssertProblemAsync(reused, HttpStatusCode.Unauthorized, "auth.invalidCode");
    }

    [Fact]
    public async Task A_remembered_device_skips_the_code_but_never_the_password()
    {
        var user = await _host.CreateUserAsync("dispositivo@example.test");
        using var client = await PasswordStepAsync(user);
        using var second = await client.PostAsync("/api/auth/login/second-factor", new { code = _host.NextCode(user), rememberDevice = true });
        await IdentityTestHost.RefreshAntiforgeryAsync(client);
        await SignOutAsync(client);

        _host.Time.Advance(TimeSpan.FromDays(10));
        using var wrongPassword = await client.PostAsync("/api/auth/login", new { email = user.Email, password = "incorrecta-1234" }); // gitleaks:allow (synthetic test value)
        using var again = await client.PostAsync("/api/auth/login", new { email = user.Email, password = IdentityTestHost.Password });

        await AssertProblemAsync(wrongPassword, HttpStatusCode.Unauthorized, "auth.invalidCredentials");
        Assert.Equal(SignInStep.Done, (await ReadAsync<LoginResponse>(again)).Next);
        Assert.True(await IsSignedInAsync(client));
    }

    [Fact]
    public async Task A_device_is_not_remembered_by_default()
    {
        var user = await _host.CreateUserAsync("sin-recordar@example.test");
        using var client = await _host.SignInAsync(user);
        await SignOutAsync(client);

        using var again = await client.PostAsync("/api/auth/login", new { email = user.Email, password = IdentityTestHost.Password });

        Assert.Equal(SignInStep.SecondFactor, (await ReadAsync<LoginResponse>(again)).Next);
    }

    [Fact]
    public async Task A_remembered_device_is_forgotten_when_the_security_stamp_changes()
    {
        var user = await _host.CreateUserAsync("sello@example.test");
        using var client = await PasswordStepAsync(user);
        using var second = await client.PostAsync("/api/auth/login/second-factor", new { code = _host.NextCode(user), rememberDevice = true });
        await IdentityTestHost.RefreshAntiforgeryAsync(client);
        await SignOutAsync(client);

        await using (var scope = _host.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            await users.UpdateSecurityStampAsync((await users.FindByIdAsync(user.Id.ToString()))!);
        }

        using var again = await client.PostAsync("/api/auth/login", new { email = user.Email, password = IdentityTestHost.Password });
        Assert.Equal(SignInStep.SecondFactor, (await ReadAsync<LoginResponse>(again)).Next);
    }

    private async Task<HttpClient> PasswordStepAsync(SyntheticUser user)
    {
        var client = await _host.NewClientAsync();
        using var response = await client.PostAsync("/api/auth/login", new { email = user.Email, password = IdentityTestHost.Password });
        Assert.Equal(SignInStep.SecondFactor, (await ReadAsync<LoginResponse>(response)).Next);
        return client;
    }

    private async Task FailCodesAsync(SyntheticUser user, int count)
    {
        using var client = await PasswordStepAsync(user);
        for (var attempt = 0; attempt < count; attempt++)
        {
            using var wrong = await client.PostAsync("/api/auth/login/second-factor", new { code = "000000", rememberDevice = false });
            await AssertProblemAsync(wrong, HttpStatusCode.Unauthorized, "auth.invalidCode");
        }
    }

    private async Task<List<string>> RecoveryCodesAsync(SyntheticUser user)
    {
        await using var scope = _host.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        return [.. (await users.GenerateNewTwoFactorRecoveryCodesAsync((await users.FindByIdAsync(user.Id.ToString()))!, 10))!];
    }

    private static async Task SignOutAsync(HttpClient client)
    {
        using var response = await client.PostAsync("/api/auth/logout", new { });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await IdentityTestHost.RefreshAntiforgeryAsync(client);
    }
}
