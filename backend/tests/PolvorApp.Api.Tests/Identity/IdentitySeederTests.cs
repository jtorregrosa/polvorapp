using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using PolvorApp.Api.Platform.Seeding;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.IdentityAccess.Endpoints;
using PolvorApp.IdentityAccess.Persistence;
using PolvorApp.IdentityAccess.Security;
using PolvorApp.IdentityAccess.Users;
using PolvorApp.SharedKernel.Seeding;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Identity;

/// <summary>Spec platform "Local environment with one command" (synthetic users) and SEC-11.</summary>
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
        Assert.Equal(IdentitySeeder.Users.Count, users.Users.Count());
        Assert.All(users.Users, u => Assert.DoesNotContain("Sintétic", u.Name, StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Joan Moltó Sala", (await users.FindByEmailAsync("jefe.uno@polvorapp.example"))!.Name);
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

    [Fact]
    public async Task The_full_dataset_adds_one_active_firing_chief_per_added_comparsa()
    {
        await using var host = await StartAsync(SeedPassword, SeedKey, SeedDataset.Full);

        Assert.Equal(0, await SeedAsync(host));
        Assert.Equal(0, await SeedAsync(host));

        await using var scope = host.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        Assert.Equal(IdentitySeeder.Users.Count + SyntheticPeople.FiringChiefs.Count, users.Users.Count());
        foreach (var chief in SyntheticPeople.FiringChiefs)
        {
            var user = await users.FindByIdAsync(chief.Id.ToString());
            Assert.NotNull(user);
            Assert.Equal((chief.Email, chief.Name, UserRole.FiringChief, UserStatus.Active), (user.Email, user.Name, user.Role, user.Status));
        }
    }

    [Fact]
    public async Task A_seed_that_fails_halfway_through_a_user_leaves_nothing_a_rerun_skips()
    {
        var fault = new FailFirstAuthenticatorKey();
        await using var host = await IdentityTestHost.StartAsync(
            postgres,
            mailpit,
            new Dictionary<string, string?>
            {
                [IdentitySeeder.PasswordKey] = SeedPassword,
                [IdentitySeeder.AuthenticatorKeyKey] = SeedKey,
                [SeedDatasets.Key] = nameof(SeedDataset.Scenarios),
            },
            services => services.ConfigureDbContext<IdentityAccessDbContext>(options => options.AddInterceptors(fault)));

        Assert.Equal(1, await SeedAsync(host));
        Assert.Equal(0, await SeedAsync(host));

        await using var scope = host.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var admin = await users.FindByEmailAsync("admin@polvorapp.example");
        Assert.NotNull(admin);
        Assert.True(await users.GetTwoFactorEnabledAsync(admin));
        Assert.Equal(SeedKey, await users.GetAuthenticatorKeyAsync(admin));
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

    private Task<IdentityTestHost> StartAsync(string? password, string? key, SeedDataset dataset = SeedDataset.Scenarios) =>
        IdentityTestHost.StartAsync(postgres, mailpit, new Dictionary<string, string?>
        {
            [IdentitySeeder.PasswordKey] = password,
            [IdentitySeeder.AuthenticatorKeyKey] = key,
            [SeedDatasets.Key] = dataset.ToString(),
        });

    private static Task<int> SeedAsync(IdentityTestHost host) =>
        SeedCommand.RunAsync(host.Services, new HostingEnvironment { EnvironmentName = Environments.Development }, TestContext.Current.CancellationToken);

    /// <summary>Fails the first save of an authenticator key, as a dropped connection would.</summary>
    private sealed class FailFirstAuthenticatorKey : SaveChangesInterceptor
    {
        private int _failed;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var savesKey = eventData.Context!.ChangeTracker.Entries<IdentityUserToken<Guid>>().Any(e => e.State == EntityState.Added);
            if (savesKey && Interlocked.Exchange(ref _failed, 1) == 0)
            {
                throw new InvalidOperationException("Simulated outage while saving the authenticator key.");
            }

            return ValueTask.FromResult(result);
        }
    }
}
