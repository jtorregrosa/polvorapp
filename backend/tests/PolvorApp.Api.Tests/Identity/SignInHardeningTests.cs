using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.IdentityAccess.Endpoints;
using PolvorApp.IdentityAccess.Persistence;
using PolvorApp.IdentityAccess.Users;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Identity;

/// <summary>
/// Findings of the group 4 security review: parallel attempts, remembered-device limit, session
/// checks that do not depend on the security stamp, and the absolute limit of a fresh sign-in.
/// </summary>
public sealed class SignInHardeningTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private IdentityTestHost _host = null!;

    public async ValueTask InitializeAsync() => _host = await IdentityTestHost.StartAsync(postgres, mailpit);

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task A_burst_of_parallel_wrong_codes_still_locks_the_account()
    {
        var user = await _host.CreateUserAsync("rafaga@example.test");
        var clients = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => PasswordStepAsync(user)));

        var responses = await Task.WhenAll(clients.Select(c => c.PostAsync("/api/auth/login/second-factor", new { code = "000000", rememberDevice = false })));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode));
        await using var scope = _host.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        Assert.True(await users.IsLockedOutAsync((await users.FindByIdAsync(user.Id.ToString()))!));
        foreach (var disposable in responses.Cast<IDisposable>().Concat(clients))
        {
            disposable.Dispose();
        }
    }

    [Fact]
    public async Task The_same_code_submitted_in_parallel_signs_in_only_once()
    {
        var user = await _host.CreateUserAsync("doble-envio@example.test");
        var code = _host.NextCode(user);
        var clients = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => PasswordStepAsync(user)));

        var responses = await Task.WhenAll(clients.Select(c => c.PostAsync("/api/auth/login/second-factor", new { code, rememberDevice = false })));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        foreach (var disposable in responses.Cast<IDisposable>().Concat(clients))
        {
            disposable.Dispose();
        }
    }

    [Fact]
    public async Task Five_wrong_recovery_codes_lock_the_account()
    {
        var user = await _host.CreateUserAsync("recuperacion-bloqueo@example.test");
        using var client = await PasswordStepAsync(user);

        for (var attempt = 0; attempt < 4; attempt++)
        {
            using var wrong = await client.PostAsync("/api/auth/login/recovery-code", new { code = "AAAAA-BBBBB" });
            await AssertProblemAsync(wrong, HttpStatusCode.Unauthorized, "auth.invalidCode");
        }

        using var fifth = await client.PostAsync("/api/auth/login/recovery-code", new { code = "AAAAA-BBBBB" });
        await AssertProblemAsync(fifth, HttpStatusCode.Unauthorized, "auth.lockedOut");
        Assert.Contains(await _host.AuditEntriesAsync("LockedOut"), e => e.EntityId == user.Id.ToString());
    }

    [Fact]
    public async Task A_wrong_second_step_is_audited_as_an_anonymous_failure()
    {
        var user = await _host.CreateUserAsync("segundo-paso-fallido@example.test");
        using var client = await PasswordStepAsync(user);

        using var wrong = await client.PostAsync("/api/auth/login/second-factor", new { code = "000000", rememberDevice = false });

        var entry = Assert.Single(await _host.AuditEntriesAsync("SignInFailed"), e => e.EntityId == user.Id.ToString());
        Assert.Null(entry.ActorUserId);
        Assert.Contains("secondFactor", entry.Data, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_remembered_device_stops_being_honoured_thirty_days_after_it_was_remembered()
    {
        var user = await _host.CreateUserAsync("treinta-dias@example.test");
        using var client = await PasswordStepAsync(user);
        using (var remembered = await client.PostAsync("/api/auth/login/second-factor", new { code = _host.NextCode(user), rememberDevice = true }))
        {
            Assert.Equal(HttpStatusCode.OK, remembered.StatusCode);
        }

        await SignOutAsync(client);
        _host.Time.Advance(TimeSpan.FromDays(20));
        using (var day20 = await client.PostAsync("/api/auth/login", new { email = user.Email, password = IdentityTestHost.Password }))
        {
            Assert.Equal(SignInStep.Done, (await ReadAsync<LoginResponse>(day20)).Next);
        }

        await SignOutAsync(client);
        _host.Time.Advance(TimeSpan.FromDays(11));
        using var day31 = await client.PostAsync("/api/auth/login", new { email = user.Email, password = IdentityTestHost.Password });
        Assert.Equal(SignInStep.SecondFactor, (await ReadAsync<LoginResponse>(day31)).Next);
    }

    [Theory]
    [InlineData("active")]
    [InlineData("two_factor_enabled")]
    public async Task A_session_ends_when_the_user_is_deactivated_or_loses_two_factor_even_without_a_stamp_change(string column)
    {
        var user = await _host.CreateUserAsync($"sin-sello-{column}@example.test");
        using var client = await _host.SignInAsync(user);

        await using (var scope = _host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityAccessDbContext>();
            var affected = column == "active"
                ? await db.Users.Where(u => u.Id == user.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.Active, false), TestContext.Current.CancellationToken)
                : await db.Users.Where(u => u.Id == user.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.TwoFactorEnabled, false), TestContext.Current.CancellationToken);
            Assert.Equal(1, affected);
        }

        Assert.False(await IsSignedInAsync(client));
    }

    [Fact]
    public async Task A_fresh_sign_in_restarts_the_twelve_hour_limit()
    {
        var user = await _host.CreateUserAsync("nueva-jornada@example.test");
        using var client = await _host.SignInAsync(user);
        for (var hours = 0; hours < 11; hours++)
        {
            _host.Time.Advance(TimeSpan.FromMinutes(59));
            Assert.True(await IsSignedInAsync(client));
        }

        await SignOutAsync(client);
        using (var login = await client.PostAsync("/api/auth/login", new { email = user.Email, password = IdentityTestHost.Password }))
        using (var second = await client.PostAsync("/api/auth/login/second-factor", new { code = _host.NextCode(user), rememberDevice = false }))
        {
            Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        }

        for (var hours = 0; hours < 3; hours++)
        {
            _host.Time.Advance(TimeSpan.FromMinutes(59));
            Assert.True(await IsSignedInAsync(client));
        }
    }

    [Fact]
    public async Task Api_responses_are_never_cached()
    {
        using var client = await _host.NewClientAsync();

        using var response = await client.GetAsync("/api/auth/enrolment", TestContext.Current.CancellationToken);

        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    private async Task<HttpClient> PasswordStepAsync(SyntheticUser user)
    {
        var client = await _host.NewClientAsync();
        using var response = await client.PostAsync("/api/auth/login", new { email = user.Email, password = IdentityTestHost.Password });
        Assert.Equal(SignInStep.SecondFactor, (await ReadAsync<LoginResponse>(response)).Next);
        return client;
    }

    private static async Task SignOutAsync(HttpClient client)
    {
        await IdentityTestHost.RefreshAntiforgeryAsync(client);
        using var response = await client.PostAsync("/api/auth/logout", new { });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await IdentityTestHost.RefreshAntiforgeryAsync(client);
    }
}
