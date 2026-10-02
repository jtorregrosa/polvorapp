using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using PolvorApp.Api.Platform.Seeding;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.IdentityAccess.Users;
using PolvorApp.SharedKernel.Modules;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Identity;

/// <summary>Findings of the group 6 and 7 reviews: step-up lockout, last-Admin counting, bootstrap and seeding guards.</summary>
public sealed class UserManagementHardeningTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private IdentityTestHost _host = null!;

    public async ValueTask InitializeAsync() => _host = await IdentityTestHost.StartAsync(postgres, mailpit);

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task Wrong_codes_on_recovery_code_regeneration_lock_the_account_and_are_audited()
    {
        var user = await _host.CreateUserAsync("paso.extra.codigo@example.test");
        using var client = await _host.SignInAsync(user);

        for (var attempt = 0; attempt < 4; attempt++)
        {
            using var wrong = await client.PostAsync("/api/account/recovery-codes", new { code = "000000" });
            await AssertProblemAsync(wrong, HttpStatusCode.BadRequest, "auth.invalidCode");
        }

        using var fifth = await client.PostAsync("/api/account/recovery-codes", new { code = "000000" });
        await AssertProblemAsync(fifth, HttpStatusCode.BadRequest, "auth.lockedOut");
        using var correct = await client.PostAsync("/api/account/recovery-codes", new { code = _host.NextCode(user) });
        await AssertProblemAsync(correct, HttpStatusCode.BadRequest, "auth.lockedOut");
        Assert.Contains(await _host.AuditEntriesAsync("LockedOut"), e => e.EntityId == user.Id.ToString());
    }

    [Fact]
    public async Task Wrong_current_passwords_lock_the_account()
    {
        var user = await _host.CreateUserAsync("paso.extra.clave@example.test");
        using var client = await _host.SignInAsync(user);

        for (var attempt = 0; attempt < 4; attempt++)
        {
            using var wrong = await client.PostAsync("/api/account/password", new { currentPassword = "no-es-la-clave", newPassword = "otra-clave-sintetica" });
            await AssertProblemAsync(wrong, HttpStatusCode.BadRequest, "account.wrongCurrentPassword");
        }

        using var fifth = await client.PostAsync("/api/account/password", new { currentPassword = "no-es-la-clave", newPassword = "otra-clave-sintetica" });
        await AssertProblemAsync(fifth, HttpStatusCode.BadRequest, "auth.lockedOut");
        using var correct = await client.PostAsync("/api/account/password", new { currentPassword = IdentityTestHost.Password, newPassword = "otra-clave-sintetica" });
        await AssertProblemAsync(correct, HttpStatusCode.BadRequest, "auth.lockedOut");
    }

    [Fact]
    public async Task An_invited_admin_does_not_count_as_another_active_admin()
    {
        var admin = await _host.CreateUserAsync("unica.admin@example.test", UserRole.Admin);
        await _host.CreateUserAsync("admin.invitada@example.test", UserRole.Admin, withPassword: false, enrolled: false);
        using var client = await _host.SignInAsync(admin);

        using var response = await client.PutAsJsonAsync(
            $"/api/users/{admin.Id}", new { name = "Admin", role = "FIRING_CHIEF", locale = "es-ES" }, TestContext.Current.CancellationToken);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "users.lastAdmin");
    }

    [Fact]
    public async Task An_invitation_token_is_not_a_reset_token()
    {
        using var admin = await _host.SignInAsync(await _host.CreateUserAsync("admin.tokens@example.test", UserRole.Admin));
        using var invite = await admin.PostAsync("/api/users", new { email = "tokens.cruzados@example.test", name = "Tokens", role = "FIRING_CHIEF", locale = "es-ES" });
        var text = (await mailpit.WaitForMessageAsync("tokens.cruzados@example.test")).Text;
        var query = text[(text.IndexOf("?user=", StringComparison.Ordinal) + 6)..].Split('\n')[0].Trim();
        var parts = query.Split("&token=");

        using var client = await _host.NewClientAsync();
        using var reset = await client.PostAsync("/api/auth/password/reset", new { userId = Guid.Parse(parts[0]), token = Uri.UnescapeDataString(parts[1]), password = "clave-cruzada-2026" }); // gitleaks:allow (synthetic test value)

        await AssertProblemAsync(reset, HttpStatusCode.Gone, "auth.invalidLink");
    }

    [Fact]
    public async Task A_malformed_invitation_link_is_an_invalid_link()
    {
        using var client = await _host.NewClientAsync();

        using var response = await client.GetAsync("/api/auth/invitations/validate?user=not-a-guid&token=x", TestContext.Current.CancellationToken);

        await AssertProblemAsync(response, HttpStatusCode.Gone, "auth.invalidLink");
    }

    [Fact]
    public async Task Create_admin_resends_to_the_same_invited_admin_and_refuses_once_an_admin_can_sign_in()
    {
        const string email = "primera.reenvio@example.test";

        Assert.Equal(0, await CreateAdminAsync("--email", email, "--name", "Primera"));
        Assert.Equal(0, await CreateAdminAsync("--email", email, "--name", "Primera"));
        await _host.DrainEmailAsync();
        Assert.Equal(2, await mailpit.CountMessagesAsync(email));

        await _host.CreateUserAsync("admin.activa.bootstrap@example.test", UserRole.Admin);
        Assert.Equal(1, await CreateAdminAsync("--email", "otra.admin@example.test", "--name", "Otra"));
        Assert.NotEmpty(await _host.AuditEntriesAsync(CreateAdminRefused));
    }

    [Fact]
    public async Task Seeding_outside_development_refuses_the_published_credentials()
    {
        await using var host = await StagingHostAsync("local-only-seed-password", "JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP");

        Assert.Equal(1, await SeedCommand.RunAsync(host.Services, new HostingEnvironment { EnvironmentName = Environments.Staging }, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Seeding_outside_development_refuses_a_database_with_other_users()
    {
        await using var host = await StagingHostAsync("staging-secret-password-value", "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ"); // gitleaks:allow (synthetic test value)
        await host.CreateUserAsync("persona.real.ficticia@example.test");

        Assert.Equal(1, await SeedCommand.RunAsync(host.Services, new HostingEnvironment { EnvironmentName = Environments.Staging }, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("=")]
    [InlineData("A")]
    [InlineData("MFRGG")]
    public async Task Seeding_refuses_a_short_authenticator_key(string key)
    {
        await using var host = await IdentityTestHost.StartAsync(postgres, mailpit, new Dictionary<string, string?>
        {
            [IdentitySeeder.PasswordKey] = "local-only-seed-password",
            [IdentitySeeder.AuthenticatorKeyKey] = key,
        });

        Assert.Equal(1, await SeedCommand.RunAsync(host.Services, new HostingEnvironment { EnvironmentName = Environments.Development }, TestContext.Current.CancellationToken));
    }

    private const string CreateAdminRefused = "AdminBootstrapRefused";

    /// <summary>A host whose environment is Staging (TLS email and https links, as Staging requires).</summary>
    private Task<IdentityTestHost> StagingHostAsync(string password, string key) =>
        IdentityTestHost.StartAsync(
            postgres,
            mailpit,
            new Dictionary<string, string?>
            {
                [IdentitySeeder.PasswordKey] = password,
                [IdentitySeeder.AuthenticatorKeyKey] = key,
                ["Email:Security"] = "StartTls",
                ["App:PublicBaseUrl"] = "https://staging.polvorapp.example",
            },
            environment: Environments.Staging);

    private Task<int> CreateAdminAsync(params string[] arguments) =>
        _host.Services.GetServices<IHostCommand>().Single(c => c.Verb == "create-admin").RunAsync(arguments, TestContext.Current.CancellationToken);
}
