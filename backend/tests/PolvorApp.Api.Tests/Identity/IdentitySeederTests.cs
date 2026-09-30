using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using PolvorApp.Api.Platform.Seeding;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.IdentityAccess.Endpoints;
using PolvorApp.IdentityAccess.Security;
using PolvorApp.IdentityAccess.Users;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Identity;

/// <summary>Spec platform "Local environment with one command" (synthetic users) and SEC-11.</summary>
[Collection(PostgresGroup.Name)]
public sealed class IdentitySeederTests(PostgresFixture postgres, MailpitFixture mailpit)
{
    private const string SeedPassword = "semilla-sintetica-local";
    private const string SeedKey = "JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP";

    [Fact]
    public async Task Seeding_creates_the_synthetic_users_and_the_admin_can_sign_in()
    {
        await using var host = await StartAsync(SeedPassword, SeedKey);

        Assert.Equal(0, await SeedAsync(host));
        Assert.Equal(0, await SeedAsync(host));

        await using var scope = host.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var statuses = new Dictionary<string, UserStatus>();
        foreach (var synthetic in IdentitySeeder.Users)
        {
            var user = await users.FindByIdAsync(synthetic.Id.ToString());
            Assert.NotNull(user);
            Assert.EndsWith("@polvorapp.example", user.Email, StringComparison.Ordinal);
            statuses[synthetic.Email] = user.Status;
        }

        Assert.Equal(3, statuses.Values.Count(s => s == UserStatus.Active));
        Assert.Contains(UserStatus.Invited, statuses.Values);
        Assert.Contains(UserStatus.Deactivated, statuses.Values);

        using var client = await host.NewClientAsync();
        using var login = await client.PostAsync("/api/auth/login", new { email = "admin@polvorapp.example", password = SeedPassword });
        Assert.Equal(SignInStep.SecondFactor, (await ReadAsync<LoginResponse>(login)).Next);
        host.Time.Advance(Totp.Step);
        var code = Totp.Compute(Totp.DecodeBase32(SeedKey), Totp.StepAt(host.Time.GetUtcNow()));
        using var second = await client.PostAsync("/api/auth/login/second-factor", new { code, rememberDevice = false });
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
    }

    [Theory]
    [InlineData(null, SeedKey, IdentitySeeder.PasswordKey)]
    [InlineData(SeedPassword, null, IdentitySeeder.AuthenticatorKeyKey)]
    [InlineData(SeedPassword, "not base32!", IdentitySeeder.AuthenticatorKeyKey)]
    public async Task Seeding_fails_naming_a_missing_or_invalid_setting(string? password, string? key, string setting)
    {
        await using var host = await StartAsync(password, key);

        Assert.Equal(1, await SeedAsync(host));

        Assert.Contains(host.Factory.Logs.Entries, e => e.Exception?.Contains(setting, StringComparison.Ordinal) ?? false);
    }

    private Task<IdentityTestHost> StartAsync(string? password, string? key) =>
        IdentityTestHost.StartAsync(postgres, mailpit, new Dictionary<string, string?>
        {
            [IdentitySeeder.PasswordKey] = password,
            [IdentitySeeder.AuthenticatorKeyKey] = key,
        });

    private static Task<int> SeedAsync(IdentityTestHost host) =>
        SeedCommand.RunAsync(host.Services, new HostingEnvironment { EnvironmentName = Environments.Development }, TestContext.Current.CancellationToken);
}
