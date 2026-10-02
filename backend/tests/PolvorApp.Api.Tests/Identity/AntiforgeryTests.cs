using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.IdentityAccess.Users;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Identity;

/// <summary>Spec "Sessions": every state-changing request needs the anti-forgery token of its session.</summary>
public sealed class AntiforgeryTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private IdentityTestHost _host = null!;

    public async ValueTask InitializeAsync() => _host = await IdentityTestHost.StartAsync(postgres, mailpit);

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task A_signed_in_write_without_the_token_is_refused_and_changes_nothing()
    {
        var admin = await _host.CreateUserAsync("admin-af@example.test", UserRole.Admin);
        var target = await _host.CreateUserAsync("objetivo-af@example.test");
        using var client = await _host.SignInAsync(admin);
        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");

        using var invite = await client.PostAsync("/api/users", new { email = "nueva-af@example.test", name = "Nueva", role = "FIRING_CHIEF", locale = "es-ES" });
        using var deactivate = await client.PostAsync($"/api/users/{target.Id}/deactivate", new { });
        using var password = await client.PostAsync("/api/account/password", new { currentPassword = IdentityTestHost.Password, newPassword = "otra-frase-larga-2026" }); // gitleaks:allow (synthetic test value)

        foreach (var response in new[] { invite, deactivate, password })
        {
            await AssertProblemAsync(response, HttpStatusCode.BadRequest, "antiforgery.invalid");
        }

        await using var scope = _host.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        Assert.Null(await users.FindByEmailAsync("nueva-af@example.test"));
        Assert.True((await users.FindByIdAsync(target.Id.ToString()))!.Active);
        Assert.True(await users.CheckPasswordAsync((await users.FindByIdAsync(admin.Id.ToString()))!, IdentityTestHost.Password));
    }

    [Fact]
    public async Task A_token_issued_before_signing_in_is_refused_afterwards()
    {
        var admin = await _host.CreateUserAsync("admin-af-antes@example.test", UserRole.Admin);
        using var client = await _host.NewClientAsync();
        var anonymousToken = client.DefaultRequestHeaders.GetValues("X-XSRF-TOKEN").Single();
        using (var login = await client.PostAsync("/api/auth/login", new { email = admin.Email, password = IdentityTestHost.Password }))
        {
            login.EnsureSuccessStatusCode();
        }

        using (var second = await client.PostAsync("/api/auth/login/second-factor", new { code = _host.NextCode(admin), rememberDevice = false }))
        {
            second.EnsureSuccessStatusCode();
        }

        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", anonymousToken);
        using var invite = await client.PostAsync("/api/users", new { email = "tarde-af@example.test", name = "Tarde", role = "FIRING_CHIEF", locale = "es-ES" });

        await AssertProblemAsync(invite, HttpStatusCode.BadRequest, "antiforgery.invalid");
    }

    [Fact]
    public async Task An_anonymous_sign_in_without_the_token_is_refused()
    {
        var user = await _host.CreateUserAsync("anonimo-af@example.test");
        using var client = await _host.NewClientAsync();
        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");

        using var login = await client.PostAsync("/api/auth/login", new { email = user.Email, password = IdentityTestHost.Password });

        await AssertProblemAsync(login, HttpStatusCode.BadRequest, "antiforgery.invalid");
        Assert.Empty(await _host.AuditEntriesAsync("SignedIn"));
    }
}
