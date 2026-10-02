using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.IdentityAccess.Endpoints;
using PolvorApp.IdentityAccess.Users;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Identity;

/// <summary>
/// Spec "Remembered devices": a remembered browser skips the code only for its own user, never past
/// a lockout, and is forgotten by every change that rotates the security stamp.
/// </summary>
public sealed class RememberedDeviceTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private const string NewPassword = "otra-frase-sintetica-2026"; // gitleaks:allow (synthetic test value)
    private IdentityTestHost _host = null!;

    public async ValueTask InitializeAsync() => _host = await IdentityTestHost.StartAsync(postgres, mailpit);

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    public static TheoryData<string> ForgettingActions => ["password change", "password reset", "deactivation", "two-factor reset"];

    [Theory]
    [MemberData(nameof(ForgettingActions))]
    public async Task A_remembered_device_is_forgotten_after(string action)
    {
        var user = await _host.CreateUserAsync($"olvido-{action.Replace(' ', '-')}@example.test");
        var admin = await _host.CreateUserAsync($"admin-olvido-{action.Replace(' ', '-')}@example.test", UserRole.Admin);
        using var device = await RememberDeviceAsync(user);
        var password = await RunAsync(action, user, admin, device);

        using var again = await device.PostAsync("/api/auth/login", new { email = user.Email, password });

        Assert.NotEqual(SignInStep.Done, (await ReadAsync<LoginResponse>(again)).Next);
        Assert.False(await IsSignedInAsync(device));
    }

    [Fact]
    public async Task A_remembered_device_skips_the_code_only_for_its_own_user()
    {
        var owner = await _host.CreateUserAsync("dueno-dispositivo@example.test");
        var other = await _host.CreateUserAsync("otra-persona@example.test");
        using var device = await RememberDeviceAsync(owner);

        using var login = await device.PostAsync("/api/auth/login", new { email = other.Email, password = IdentityTestHost.Password });

        Assert.Equal(SignInStep.SecondFactor, (await ReadAsync<LoginResponse>(login)).Next);
        Assert.False(await IsSignedInAsync(device));
    }

    [Fact]
    public async Task A_remembered_device_does_not_get_past_a_lockout()
    {
        var user = await _host.CreateUserAsync("bloqueo-recordado@example.test");
        using var device = await RememberDeviceAsync(user);
        using var attacker = await _host.NewClientAsync();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var failed = await attacker.PostAsync("/api/auth/login", new { email = user.Email, password = "incorrecta-1234" }); // gitleaks:allow (synthetic test value)
        }

        using var login = await device.PostAsync("/api/auth/login", new { email = user.Email, password = IdentityTestHost.Password });

        await AssertProblemAsync(login, HttpStatusCode.Unauthorized, "auth.invalidCredentials");
        Assert.False(await IsSignedInAsync(device));
    }

    /// <summary>Signs in with "remember this device", then signs out, keeping the browser's cookies.</summary>
    private async Task<HttpClient> RememberDeviceAsync(SyntheticUser user)
    {
        var client = await _host.NewClientAsync();
        using (var login = await client.PostAsync("/api/auth/login", new { email = user.Email, password = IdentityTestHost.Password }))
        {
            Assert.Equal(SignInStep.SecondFactor, (await ReadAsync<LoginResponse>(login)).Next);
        }

        using (var second = await client.PostAsync("/api/auth/login/second-factor", new { code = _host.NextCode(user), rememberDevice = true }))
        {
            second.EnsureSuccessStatusCode();
        }

        await IdentityTestHost.RefreshAntiforgeryAsync(client);
        using (var logout = await client.PostAsync("/api/auth/logout", new { }))
        {
            Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        }

        await IdentityTestHost.RefreshAntiforgeryAsync(client);
        using var remembered = await client.PostAsync("/api/auth/login", new { email = user.Email, password = IdentityTestHost.Password });
        Assert.Equal(SignInStep.Done, (await ReadAsync<LoginResponse>(remembered)).Next);
        await IdentityTestHost.RefreshAntiforgeryAsync(client);
        using (var logout = await client.PostAsync("/api/auth/logout", new { }))
        {
            Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        }

        await IdentityTestHost.RefreshAntiforgeryAsync(client);
        return client;
    }

    /// <summary>Performs <paramref name="action"/> through the API; returns the user's password afterwards.</summary>
    private async Task<string> RunAsync(string action, SyntheticUser user, SyntheticUser admin, HttpClient device)
    {
        switch (action)
        {
            case "password change":
                {
                    using var session = await _host.SignInAsync(user);
                    using var change = await session.PostAsync("/api/account/password", new { currentPassword = IdentityTestHost.Password, newPassword = NewPassword });
                    Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);
                    return NewPassword;
                }

            case "password reset":
                {
                    string token;
                    await using (var scope = _host.Services.CreateAsyncScope())
                    {
                        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
                        token = await users.GeneratePasswordResetTokenAsync((await users.FindByIdAsync(user.Id.ToString()))!);
                    }

                    using var reset = await device.PostAsync("/api/auth/password/reset", new { userId = user.Id, token, password = NewPassword });
                    Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
                    return NewPassword;
                }

            case "deactivation":
                {
                    using var session = await _host.SignInAsync(admin);
                    using var deactivate = await session.PostAsync($"/api/users/{user.Id}/deactivate", new { });
                    Assert.True(deactivate.IsSuccessStatusCode);
                    using var reactivate = await session.PostAsync($"/api/users/{user.Id}/reactivate", new { });
                    Assert.True(reactivate.IsSuccessStatusCode);
                    return IdentityTestHost.Password;
                }

            case "two-factor reset":
                {
                    using var session = await _host.SignInAsync(admin);
                    using var reset = await session.PostAsync($"/api/users/{user.Id}/two-factor/reset", new { });
                    Assert.True(reset.IsSuccessStatusCode);
                    return IdentityTestHost.Password;
                }

            default:
                throw new ArgumentOutOfRangeException(nameof(action), action, null);
        }
    }
}
