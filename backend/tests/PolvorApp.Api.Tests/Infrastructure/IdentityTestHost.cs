using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using PolvorApp.Api.Platform.Database;
using PolvorApp.Api.Platform.Email;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.IdentityAccess.Security;
using PolvorApp.IdentityAccess.Users;

namespace PolvorApp.Api.Tests.Infrastructure;

/// <summary>
/// The API on its own migrated database, with a controllable clock and email delivered to Mailpit.
/// xUnit creates one host per test (each test class instance starts its own); tests still use
/// distinct synthetic users. Identity's lockout and token expiry read the system clock; sessions,
/// TOTP and remembered devices read <see cref="Time"/>.
/// </summary>
public sealed class IdentityTestHost : IAsyncDisposable
{
    public const string Password = "sintetica-larga-2026"; // gitleaks:allow (synthetic test value)

    private IdentityTestHost(ApiFactory factory, FakeTimeProvider time)
    {
        Factory = factory;
        Time = time;
    }

    public ApiFactory Factory { get; }

    public FakeTimeProvider Time { get; }

    public IServiceProvider Services => Factory.Services;

    public static async Task<IdentityTestHost> StartAsync(
        PostgresFixture postgres,
        MailpitFixture mailpit,
        IReadOnlyDictionary<string, string?>? settings = null,
        Action<IServiceCollection>? configureServices = null,
        string environment = "Development")
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var all = new Dictionary<string, string?>(mailpit.Settings)
        {
            // Generous limits so tests are not throttled; rate limiting has its own tests.
            ["RateLimits:Auth:PermitLimit"] = "1000",
            ["RateLimits:AuthEmail:PermitLimit"] = "1000",
        };
        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
        {
            all[key] = value;
        }

        var factory = new ApiFactory(
            await postgres.CreateDatabaseAsync(),
            environment,
            settings: all,
            configureServices: services =>
            {
                services.AddSingleton<TimeProvider>(time);
                configureServices?.Invoke(services);
            });
        var exitCode = await MigrateCommand.RunAsync(factory.Services, TestContext.Current.CancellationToken);
        if (exitCode != 0)
        {
            throw new InvalidOperationException("Migrating the identity test database failed.");
        }

        return new IdentityTestHost(factory, time);
    }

    public ValueTask DisposeAsync() => Factory.DisposeAsync();

    /// <summary>Waits until every queued email has been handed to the SMTP server (or failed).</summary>
    public Task DrainEmailAsync() =>
        Services.GetRequiredService<BackgroundEmailOutbox>().DrainAsync(TestContext.Current.CancellationToken);

    /// <summary>A browser-like client: keeps cookies and sends the anti-forgery header on writes.</summary>
    public async Task<HttpClient> NewClientAsync()
    {
        var client = Factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, AllowAutoRedirect = false });
        await RefreshAntiforgeryAsync(client);
        return client;
    }

    /// <summary>Anti-forgery tokens are bound to the signed-in user: refresh after every sign-in or sign-out.</summary>
    public static async Task RefreshAntiforgeryAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/auth/antiforgery", TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var token = response.Headers.GetValues("Set-Cookie")
            .Select(cookie => cookie.Split(';')[0])
            .Single(cookie => cookie.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal))["XSRF-TOKEN=".Length..];
        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", Uri.UnescapeDataString(token));
    }

    /// <summary>Creates a user; by default active with a password and an enrolled authenticator.</summary>
    public async Task<SyntheticUser> CreateUserAsync(
        string email, UserRole role = UserRole.FiringChief, bool withPassword = true, bool enrolled = true, bool active = true, string locale = "es-ES")
    {
        await using var scope = Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var user = new User
        {
            UserName = email,
            Email = email,
            Name = $"Persona Sintética {email.Split('@')[0]}",
            Role = role,
            Locale = locale,
            Active = active,
            CreatedAt = Time.GetUtcNow(),
        };
        Check(withPassword ? await users.CreateAsync(user, Password) : await users.CreateAsync(user));

        string? key = null;
        if (enrolled)
        {
            Check(await users.ResetAuthenticatorKeyAsync(user));
            key = await users.GetAuthenticatorKeyAsync(user);
            Check(await users.SetTwoFactorEnabledAsync(user, true));
        }

        return new SyntheticUser(user.Id, email, key);
    }

    /// <summary>
    /// Advances the clock one TOTP step, then returns <paramref name="user"/>'s current code, so a
    /// new code is never a replay of the previous one. Time-based assertions must account for it.
    /// </summary>
    public string NextCode(SyntheticUser user)
    {
        Time.Advance(Totp.Step);
        return Totp.Compute(Totp.DecodeBase32(user.AuthenticatorKey!), Totp.StepAt(Time.GetUtcNow()));
    }

    /// <summary>Password step followed by the authenticator step: a full sign-in.</summary>
    public async Task<HttpClient> SignInAsync(SyntheticUser user)
    {
        var client = await NewClientAsync();
        using (var login = await client.PostAsJsonAsync("/api/auth/login", new { email = user.Email, password = Password }, TestContext.Current.CancellationToken))
        {
            login.EnsureSuccessStatusCode();
        }

        using (var second = await client.PostAsJsonAsync("/api/auth/login/second-factor", new { code = NextCode(user), rememberDevice = false }, TestContext.Current.CancellationToken))
        {
            second.EnsureSuccessStatusCode();
        }

        await RefreshAntiforgeryAsync(client);
        return client;
    }

    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(string.Join(", ", result.Errors.Select(e => e.Code)));
        }
    }
}

public sealed record SyntheticUser(Guid Id, string Email, string? AuthenticatorKey);
