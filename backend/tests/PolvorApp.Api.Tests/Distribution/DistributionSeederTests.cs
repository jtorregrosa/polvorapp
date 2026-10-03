using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using PolvorApp.Api.Platform.Seeding;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.Distribution.Contracts;
using PolvorApp.Distribution.Persistence;
using PolvorApp.Distribution.Seeding;
using PolvorApp.FestivalEditions.Seeding;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.IdentityAccess.Users;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Distribution;

/// <summary>Spec "Synthetic distribution data": both days with slots, two proxies that hold, fictional and safe to run again.</summary>
public sealed class DistributionSeederTests(PostgresFixture postgres, MailpitFixture mailpit, MinioFixture minio)
{
    private const string SeedPassword = "semilla-sintetica-local";
    private const string SeedKey = "JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP";

    private static readonly Guid Norte = new("0193a100-0000-7000-8000-000000000001");
    private static readonly Guid Sur = new("0193a100-0000-7000-8000-000000000002");
    private static readonly Guid Este = new("0193a100-0000-7000-8000-000000000003");

    [Fact]
    public async Task Seeding_twice_creates_the_days_slots_and_proxies_once()
    {
        await using var host = await StartAsync();

        Assert.Equal(0, await SeedAsync(host));
        Assert.Equal(0, await SeedAsync(host));

        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DistributionDbContext>();
        var days = await db.Days.AsNoTracking().Include(d => d.Slots).Where(d => d.EditionId == EditionSeeder.CurrentEdition).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal([DistributionType.Powder, DistributionType.Weapons], days.Select(d => d.Type).Order());
        Assert.All(days, d => Assert.Equal(new[] { Norte, Sur }.Order(), d.Slots.Select(s => s.ComparsaId).Order()));
        Assert.All(days, d => Assert.DoesNotContain(d.Slots, s => s.ComparsaId == Este));
        Assert.Equal(DistributionSeeder.ProxyCount, await db.Proxies.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_seeded_proxies_hold_and_the_seeded_days_belong_to_the_edition()
    {
        await using var host = await StartAsync();
        Assert.Equal(0, await SeedAsync(host));
        using var admin = await host.SignInAsync(await host.CreateUserAsync("admin.semilla.reparto@example.test", UserRole.Admin));

        using var response = await admin.GetAsync($"/api/distribution/editions/{EditionSeeder.CurrentEdition}/proxies", TestContext.Current.CancellationToken);

        var proxies = (await ReadAsync<JsonElement>(response)).EnumerateArray().ToList();
        Assert.Equal(["POWDER", "WEAPONS"], proxies.Select(p => p.GetProperty("type").GetString()!).Order());
        Assert.All(proxies, p => Assert.Equal(JsonValueKind.Null, p.GetProperty("problem").ValueKind));
        var plan = await DistributionRequests.PlanOfAsync(admin, EditionSeeder.CurrentEdition);
        Assert.All(plan.GetProperty("days").EnumerateArray(), d => Assert.False(string.IsNullOrWhiteSpace(d.GetProperty("location").GetString())));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private Task<IdentityTestHost> StartAsync()
    {
        var settings = new Dictionary<string, string?> { [IdentitySeeder.PasswordKey] = SeedPassword, [IdentitySeeder.AuthenticatorKeyKey] = SeedKey };
        foreach (var (key, value) in minio.SettingsFor("polvorapp-test-reparto"))
        {
            settings[key] = value;
        }

        return IdentityTestHost.StartAsync(postgres, mailpit, settings);
    }

    private static Task<int> SeedAsync(IdentityTestHost host) =>
        SeedCommand.RunAsync(host.Services, new HostingEnvironment { EnvironmentName = Environments.Development }, TestContext.Current.CancellationToken);
}
