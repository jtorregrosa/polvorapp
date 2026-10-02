using System.Net;
using System.Net.Http.Json;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.IdentityAccess.Endpoints;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Identity;

/// <summary>Spec "Account self-service" and platform "Switch UI language" (saved locale).</summary>
public sealed class AccountTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private const string NewPassword = "cambiada-sintetica-2026"; // gitleaks:allow (synthetic test value)
    private IdentityTestHost _host = null!;

    public async ValueTask InitializeAsync() => _host = await IdentityTestHost.StartAsync(postgres, mailpit);

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task The_account_shows_name_email_role_and_language()
    {
        var user = await _host.CreateUserAsync("mi.cuenta@example.test", locale: "ca-ES-valencia");
        using var client = await _host.SignInAsync(user);

        using var response = await client.GetAsync("/api/account", TestContext.Current.CancellationToken);

        var account = await ReadAsync<AccountResponse>(response);
        Assert.Equal(("mi.cuenta@example.test", "ca-ES-valencia"), (account.Email, account.Locale));
    }

    [Fact]
    public async Task Changing_the_language_saves_it_for_later_emails()
    {
        var user = await _host.CreateUserAsync("idioma@example.test");
        using var client = await _host.SignInAsync(user);

        using var update = await client.PutAsJsonAsync("/api/account/locale", new { locale = "en" }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);

        using var forgot = await (await _host.NewClientAsync()).PostAsync("/api/auth/password/forgot", new { email = user.Email });
        Assert.Equal("Reset your PolvorApp password", (await mailpit.WaitForMessageAsync(user.Email)).Subject);
    }

    [Fact]
    public async Task An_unsupported_language_is_refused()
    {
        using var client = await _host.SignInAsync(await _host.CreateUserAsync("idioma.malo@example.test"));

        using var update = await client.PutAsJsonAsync("/api/account/locale", new { locale = "fr-FR" }, TestContext.Current.CancellationToken);

        await AssertProblemAsync(update, HttpStatusCode.BadRequest, "validation");
    }

    [Fact]
    public async Task Changing_the_password_keeps_this_session_and_ends_the_others()
    {
        var user = await _host.CreateUserAsync("cambio.clave@example.test");
        using var current = await _host.SignInAsync(user);
        using var other = await _host.SignInAsync(user);

        using var change = await current.PostAsync("/api/account/password", new { currentPassword = IdentityTestHost.Password, newPassword = NewPassword });

        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);
        Assert.True(await IsSignedInAsync(current));
        Assert.False(await IsSignedInAsync(other));
        Assert.Contains(await _host.AuditEntriesAsync("PasswordChanged"), e => e.EntityId == user.Id.ToString());
    }

    [Fact]
    public async Task A_wrong_current_password_changes_nothing()
    {
        var user = await _host.CreateUserAsync("clave.actual.mal@example.test");
        using var client = await _host.SignInAsync(user);

        using var change = await client.PostAsync("/api/account/password", new { currentPassword = "no-es-la-clave", newPassword = NewPassword });

        await AssertProblemAsync(change, HttpStatusCode.BadRequest, "account.wrongCurrentPassword");
        Assert.True(await IsSignedInAsync(client));
    }

    [Fact]
    public async Task Regenerating_recovery_codes_needs_a_valid_code_and_replaces_the_old_ones()
    {
        var user = await _host.CreateUserAsync("nuevos.codigos@example.test");
        using var client = await _host.SignInAsync(user);

        using var wrong = await client.PostAsync("/api/account/recovery-codes", new { code = "000000" });
        await AssertProblemAsync(wrong, HttpStatusCode.BadRequest, "auth.invalidCode");

        using var first = await client.PostAsync("/api/account/recovery-codes", new { code = _host.NextCode(user) });
        var old = (await ReadAsync<RecoveryCodesResponse>(first)).RecoveryCodes;
        using var second = await client.PostAsync("/api/account/recovery-codes", new { code = _host.NextCode(user) });
        var fresh = (await ReadAsync<RecoveryCodesResponse>(second)).RecoveryCodes;

        Assert.Equal(10, fresh.Count);
        Assert.Empty(old.Intersect(fresh));
        using var signIn = await _host.NewClientAsync();
        using var login = await signIn.PostAsync("/api/auth/login", new { email = user.Email, password = IdentityTestHost.Password });
        using var oldCode = await signIn.PostAsync("/api/auth/login/recovery-code", new { code = old[0] });
        await AssertProblemAsync(oldCode, HttpStatusCode.Unauthorized, "auth.invalidCode");
    }

    [Fact]
    public async Task Signing_out_everywhere_ends_every_session_and_remembered_device()
    {
        var user = await _host.CreateUserAsync("salir.todo@example.test");
        using var current = await _host.SignInAsync(user);
        using var remembered = await _host.NewClientAsync();
        using (var login = await remembered.PostAsync("/api/auth/login", new { email = user.Email, password = IdentityTestHost.Password }))
        using (var second = await remembered.PostAsync("/api/auth/login/second-factor", new { code = _host.NextCode(user), rememberDevice = true }))
        {
            Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        }

        using var response = await current.PostAsync("/api/account/sign-out-everywhere", new { });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(await IsSignedInAsync(current));
        Assert.False(await IsSignedInAsync(remembered));
        await IdentityTestHost.RefreshAntiforgeryAsync(remembered);
        using var again = await remembered.PostAsync("/api/auth/login", new { email = user.Email, password = IdentityTestHost.Password });
        Assert.Equal(SignInStep.SecondFactor, (await ReadAsync<LoginResponse>(again)).Next);
    }
}
