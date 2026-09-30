using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using PolvorApp.Api.Platform.Seeding;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.FederationCatalog.Comparsas;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.FederationCatalog.Endpoints;
using PolvorApp.FederationCatalog.Persistence;
using PolvorApp.FederationCatalog.Seeding;
using PolvorApp.FederationCatalog.WeaponModels;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.IdentityAccess.Endpoints;
using PolvorApp.IdentityAccess.Security;
using PolvorApp.IdentityAccess.Users;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Catalog;

/// <summary>Spec "Synthetic catalogue data" (SEC-11): fictional, deterministic, safe to run again.</summary>
[Collection(PostgresGroup.Name)]
public sealed class CatalogSeederTests(PostgresFixture postgres, MailpitFixture mailpit)
{
    private const string SeedPassword = "semilla-sintetica-local";
    private const string SeedKey = "JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP";

    [Fact]
    public async Task Seeding_twice_creates_the_fictional_catalogue_once()
    {
        await using var host = await StartAsync();

        Assert.Equal(0, await SeedAsync(host));
        Assert.Equal(0, await SeedAsync(host));

        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FederationCatalogDbContext>();
        var comparsas = await db.Comparsas.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(4, comparsas.Count);
        Assert.All(comparsas, c => Assert.StartsWith("Comparsa Sintética", c.Name, StringComparison.Ordinal));
        Assert.Equal([Side.Moorish, Side.Christian], comparsas.Select(c => c.Side).Distinct().Order());
        Assert.Single(comparsas, c => !c.Active);

        var models = await db.WeaponModels.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(9, models.Count);
        Assert.Equal(Enum.GetValues<WeaponKind>(), models.Select(m => m.Kind).Distinct().Order());
        Assert.Single(models, m => !m.Active);
        Assert.False(Assert.Single(models, m => m.Kind == WeaponKind.Pistol).Rentable);

        var assignments = await db.Assignments.AsNoTracking().Select(a => new { a.ComparsaId, a.UserId }).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            CatalogSeeder.Assignments.ToHashSet(),
            assignments.Select(a => (a.ComparsaId, a.UserId)).ToHashSet());
        Assert.Equal(
            [(CatalogSeeder.Norte, CatalogSeeder.JefeUno), (CatalogSeeder.Sur, CatalogSeeder.JefeUno), (CatalogSeeder.Norte, CatalogSeeder.JefaDos)],
            CatalogSeeder.Assignments);
    }

    [Fact]
    public async Task The_seeded_firing_chief_sees_only_the_seeded_comparsas_assigned_to_them()
    {
        await using var host = await StartAsync();
        Assert.Equal(0, await SeedAsync(host));
        using var client = await host.NewClientAsync();
        using (var login = await client.PostAsync("/api/auth/login", new { email = "jefe.uno@polvorapp.example", password = SeedPassword }))
        {
            Assert.Equal(SignInStep.SecondFactor, (await ReadAsync<LoginResponse>(login)).Next);
        }

        host.Time.Advance(Totp.Step);
        var code = Totp.Compute(Totp.DecodeBase32(SeedKey), Totp.StepAt(host.Time.GetUtcNow()));
        using (var second = await client.PostAsync("/api/auth/login/second-factor", new { code, rememberDevice = false }))
        {
            Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        }

        using var response = await client.GetAsync("/api/comparsas?includeInactive=true", TestContext.Current.CancellationToken);

        Assert.Equal(["Comparsa Sintética Norte", "Comparsa Sintética Sur"], (await ReadAsync<List<ComparsaResponse>>(response)).Select(c => c.Name));
    }

    [Fact]
    public void The_seeded_firing_chiefs_are_the_identity_seeders_users()
    {
        Assert.Equal(IdentitySeeder.Users.Single(u => u.Email == "jefe.uno@polvorapp.example").Id, CatalogSeeder.JefeUno);
        Assert.Equal(IdentitySeeder.Users.Single(u => u.Email == "jefa.dos@polvorapp.example").Id, CatalogSeeder.JefaDos);
    }

    [Fact]
    public async Task Rows_whose_name_label_or_combination_is_taken_are_skipped_and_the_rest_is_seeded()
    {
        await using var host = await StartAsync();
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FederationCatalogDbContext>();
            db.Comparsas.Add(new Comparsa { Id = Guid.CreateVersion7(), Name = "comparsa sintética norte", Side = Side.Christian, CreatedAt = host.Time.GetUtcNow() });
            db.WeaponModels.Add(new WeaponModel { Id = Guid.CreateVersion7(), Kind = WeaponKind.Trabuco, Side = Side.Christian, Handedness = Handedness.Right, Size = WeaponSize.Normal, Rentable = true, Label = "Trabuco de prueba", CreatedAt = host.Time.GetUtcNow() });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        Assert.Equal(0, await SeedAsync(host));

        await using var check = host.Services.CreateAsyncScope();
        var seeded = check.ServiceProvider.GetRequiredService<FederationCatalogDbContext>();
        Assert.Equal(4, await seeded.Comparsas.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(9, await seeded.WeaponModels.CountAsync(TestContext.Current.CancellationToken));
        Assert.DoesNotContain(await seeded.Assignments.Select(a => a.ComparsaId).ToListAsync(TestContext.Current.CancellationToken), id => id == CatalogSeeder.Norte);
    }

    [Theory]
    [InlineData("comparsa")]
    [InlineData("model")]
    [InlineData("assignment")]
    public async Task Outside_local_environments_a_database_with_real_catalogue_data_is_refused_before_writing(string real)
    {
        await using var host = await StartAsync();
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FederationCatalogDbContext>();
        var staging = new CatalogSeeder(db, host.Time, new HostingEnvironment { EnvironmentName = "Staging" }, NullLogger<CatalogSeeder>.Instance);
        await staging.SeedAsync(TestContext.Current.CancellationToken);
        var comparsas = await db.Comparsas.CountAsync(TestContext.Current.CancellationToken);
        switch (real)
        {
            case "comparsa":
                db.Comparsas.Add(new Comparsa { Id = Guid.CreateVersion7(), Name = "Comparsa No Sintética", Side = Side.Moorish, CreatedAt = host.Time.GetUtcNow() });
                comparsas++;
                break;
            case "model":
                db.WeaponModels.Add(new WeaponModel { Id = Guid.CreateVersion7(), Kind = WeaponKind.Pistol, Label = "PISTOLA NO SINTÉTICA", CreatedAt = host.Time.GetUtcNow() });
                break;
            default:
                await host.AssignAsync(CatalogSeeder.Sur, Guid.CreateVersion7());
                break;
        }

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.ChangeTracker.Clear();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => staging.SeedAsync(TestContext.Current.CancellationToken));

        Assert.Contains("not synthetic", error.Message, StringComparison.Ordinal);
        Assert.Equal(comparsas, await db.Comparsas.CountAsync(TestContext.Current.CancellationToken));
    }

    private Task<IdentityTestHost> StartAsync() =>
        IdentityTestHost.StartAsync(
            postgres, mailpit, new Dictionary<string, string?> { [IdentitySeeder.PasswordKey] = SeedPassword, [IdentitySeeder.AuthenticatorKeyKey] = SeedKey });

    private static Task<int> SeedAsync(IdentityTestHost host) =>
        SeedCommand.RunAsync(host.Services, new HostingEnvironment { EnvironmentName = Environments.Development }, TestContext.Current.CancellationToken);
}
