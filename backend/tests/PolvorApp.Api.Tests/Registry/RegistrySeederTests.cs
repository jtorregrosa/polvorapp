using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using PolvorApp.Api.Platform.Seeding;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ArquebusierRegistry.Insights;
using PolvorApp.ArquebusierRegistry.NationalIds;
using PolvorApp.ArquebusierRegistry.Persistence;
using PolvorApp.ArquebusierRegistry.Seeding;
using PolvorApp.ComplianceInsights;
using PolvorApp.ComplianceInsights.Contracts;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.FederationCatalog.Persistence;
using PolvorApp.FederationCatalog.Seeding;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.IdentityAccess.Endpoints;
using PolvorApp.IdentityAccess.Security;
using PolvorApp.IdentityAccess.Users;
using PolvorApp.SharedKernel.Images;
using PolvorApp.SharedKernel.Storage;
using PolvorApp.SharedKernel.Time;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Registry;

/// <summary>Spec "Synthetic registry data" (SEC-11, design D9): fictional, deterministic, safe to run again.</summary>
[Collection(PostgresGroup.Name)]
public sealed class RegistrySeederTests(PostgresFixture postgres, MailpitFixture mailpit, MinioFixture minio)
{
    private const string SeedPassword = "semilla-sintetica-local";
    private const string SeedKey = "JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP";

    [Fact]
    public async Task Seeding_twice_creates_the_synthetic_registry_once()
    {
        await using var host = await StartAsync();

        Assert.Equal(0, await SeedAsync(host));
        Assert.Equal(0, await SeedAsync(host));

        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ArquebusierRegistryDbContext>();
        var arquebusiers = await db.Arquebusiers.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken);
        var weapons = await db.OwnedWeapons.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(RegistrySeeder.ArquebusierCount, arquebusiers.Count);
        Assert.Equal(RegistrySeeder.OwnedWeaponCount, weapons.Count);
        Assert.Equal(arquebusiers.Count, arquebusiers.Select(a => a.Id).Distinct().Count());
    }

    [Fact]
    public async Task The_seed_covers_every_status_license_state_and_weapon_kind_with_valid_synthetic_ids()
    {
        await using var host = await StartAsync();
        Assert.Equal(0, await SeedAsync(host));

        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ArquebusierRegistryDbContext>();
        var arquebusiers = await db.Arquebusiers.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken);
        var today = FederationCalendar.Today(host.Time);

        Assert.All(arquebusiers, a => Assert.Equal(a.NationalId, NationalId.Parse(a.NationalId).Value));
        Assert.Contains(arquebusiers, a => a.NationalId.StartsWith('X'));
        Assert.All(arquebusiers, a => Assert.Contains("Sintétic", a.LastName, StringComparison.Ordinal));
        Assert.All(arquebusiers.Where(a => a.Email is not null), a => Assert.EndsWith("@polvorapp.example", a.Email!, StringComparison.Ordinal));
        Assert.Equal(Enum.GetValues<ArquebusierStatus>(), arquebusiers.Select(a => a.Status).Distinct().Order());
        Assert.Equal(
            new LicenseStatus?[] { null, LicenseStatus.Pending, LicenseStatus.Valid, LicenseStatus.Expired }.Order(),
            arquebusiers.Select(a => a.CurrentLicense()?.StatusOn(today)).Distinct().Order());
        Assert.Contains(arquebusiers, a => a.LicenseType == LicenseType.AProf);
        Assert.Contains(arquebusiers, a => a.TrainingCompletedOn is null);
        Assert.Contains(arquebusiers, a => a.TrainingCompletedOn is not null);
        Assert.Contains(arquebusiers, a => a.ComparsaId == CatalogSeeder.Norte);
        Assert.Contains(arquebusiers, a => a.ComparsaId == CatalogSeeder.Sur);

        var modelIds = await db.OwnedWeapons.AsNoTracking().Select(w => w.WeaponModelId).ToListAsync(TestContext.Current.CancellationToken);
        var catalog = scope.ServiceProvider.GetRequiredService<FederationCatalogDbContext>();
        var kinds = await catalog.WeaponModels.AsNoTracking().Where(m => modelIds.Contains(m.Id)).Select(m => m.Kind).Distinct().ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(Enum.GetValues<WeaponKind>(), kinds.Order());
    }

    /// <summary>Spec "Synthetic registry data": together the seeded arquebusiers show every compliance warning.</summary>
    [Fact]
    public async Task The_seed_shows_every_compliance_warning()
    {
        await using var host = await StartAsync();
        Assert.Equal(0, await SeedAsync(host));

        await using var scope = host.Services.CreateAsyncScope();
        var evaluated = await scope.ServiceProvider.GetRequiredService<ScopedFacts>().ReadAsync(ComparsaAccess.All, TestContext.Current.CancellationToken);

        var seen = evaluated.SelectMany(e => e.Warnings).ToHashSet();
        Assert.All(Enum.GetValues<ComplianceWarning>(), warning => Assert.Contains(warning, seen));
    }

    [Theory]
    [InlineData(14, new[] { ComplianceWarning.LicenseExpiring, ComplianceWarning.CourseMissing, ComplianceWarning.UnderAge, ComplianceWarning.IdPhotoMissing })]
    [InlineData(7, new[] { ComplianceWarning.LicensePhotosMissing })]
    public async Task Seeded_arquebusiers_have_their_planned_warnings(int number, ComplianceWarning[] expected)
    {
        await using var host = await StartAsync();
        Assert.Equal(0, await SeedAsync(host));

        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ArquebusierRegistryDbContext>();
        var id = new Guid($"0193a300-0000-7000-8000-{number:D12}");
        var arquebusier = await db.Arquebusiers.AsNoTracking().SingleAsync(a => a.Id == id, TestContext.Current.CancellationToken);
        var kinds = await db.Photos.AsNoTracking().Where(p => p.ArquebusierId == id).Select(p => p.Kind).ToListAsync(TestContext.Current.CancellationToken);
        var facts = RegistryCompliance.FactsOf(
            id,
            arquebusier.BirthDate,
            arquebusier.TrainingCompletedOn,
            new LicenseColumns(arquebusier.LicenseType, arquebusier.LicensePending, arquebusier.LicenseExpiresOn),
            new PhotoFlags(kinds.Contains(ArquebusierPhotoKind.Id), kinds.Contains(ArquebusierPhotoKind.LicenseFront), kinds.Contains(ArquebusierPhotoKind.LicenseBack)));

        var warnings = scope.ServiceProvider.GetRequiredService<IComplianceRules>().Evaluate(facts, FederationCalendar.Today(host.Time));

        Assert.Equal(expected, warnings);
    }

    [Fact]
    public async Task The_seeded_FiringChief_sees_only_the_arquebusiers_of_their_comparsa()
    {
        await using var host = await StartAsync();
        Assert.Equal(0, await SeedAsync(host));
        using var client = await host.NewClientAsync();
        using (var login = await client.PostAsync("/api/auth/login", new { email = "jefa.dos@polvorapp.example", password = SeedPassword }))
        {
            Assert.Equal(SignInStep.SecondFactor, (await ReadAsync<LoginResponse>(login)).Next);
        }

        host.Time.Advance(Totp.Step);
        var code = Totp.Compute(Totp.DecodeBase32(SeedKey), Totp.StepAt(host.Time.GetUtcNow()));
        using (var second = await client.PostAsync("/api/auth/login/second-factor", new { code, rememberDevice = false }))
        {
            Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        }

        using var response = await client.GetAsync("/api/arquebusiers", TestContext.Current.CancellationToken);

        var rows = await ReadAsync<List<JsonElement>>(response);
        Assert.NotEmpty(rows);
        Assert.All(rows, r => Assert.Equal(CatalogSeeder.Norte, r.GetProperty("comparsaId").GetGuid()));
    }

    [Fact]
    public async Task Outside_local_environments_a_database_with_real_arquebusiers_is_refused_before_writing()
    {
        await using var host = await StartAsync();
        Assert.Equal(0, await SeedAsync(host));
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ArquebusierRegistryDbContext>();
        db.Arquebusiers.Add(RegistryData.NewArquebusier(CatalogSeeder.Sur, lastName: "No Sintético"));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.ChangeTracker.Clear();
        var count = await db.Arquebusiers.CountAsync(TestContext.Current.CancellationToken);
        var staging = NewSeeder(scope, host, "Staging");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => staging.SeedAsync(TestContext.Current.CancellationToken));

        Assert.Contains("not synthetic", error.Message, StringComparison.Ordinal);
        Assert.Equal(count, await db.Arquebusiers.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Outside_local_environments_a_database_with_a_real_owned_weapon_is_refused()
    {
        await using var host = await StartAsync();
        Assert.Equal(0, await SeedAsync(host));
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ArquebusierRegistryDbContext>();
        var owner = await db.Arquebusiers.AsNoTracking().FirstAsync(TestContext.Current.CancellationToken);
        var model = await db.OwnedWeapons.AsNoTracking().Select(w => w.WeaponModelId).FirstAsync(TestContext.Current.CancellationToken);
        db.OwnedWeapons.Add(RegistryData.NewOwnedWeapon(owner.Id, model, "NO-SINTETICA-1"));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var staging = NewSeeder(scope, host, "Staging");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => staging.SeedAsync(TestContext.Current.CancellationToken));

        Assert.Contains("not synthetic", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_the_catalogue_seed_the_registry_seed_skips_what_it_cannot_place()
    {
        await using var host = await StartAsync();
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ArquebusierRegistryDbContext>();
        var seeder = NewSeeder(scope, host, Environments.Development);

        await seeder.SeedAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, await db.Arquebusiers.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Some_seeded_arquebusiers_get_synthetic_photos_and_others_none()
    {
        var bucket = $"seed-{Guid.NewGuid():N}";
        await using var host = await StartAsync(bucket);
        Assert.Equal(0, await SeedAsync(host));

        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ArquebusierRegistryDbContext>();
        var photos = await db.Photos.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(RegistrySeeder.PhotoCount, photos.Count);
        Assert.Equal(7, photos.Count(p => p.Kind == ArquebusierPhotoKind.Id));
        Assert.Equal([ArquebusierPhotoKind.Id, ArquebusierPhotoKind.LicenseFront], photos.Where(p => p.ArquebusierId == new Guid("0193a300-0000-7000-8000-000000000007")).Select(p => p.Kind).Order());
        Assert.Equal(
            [ArquebusierPhotoKind.Id, ArquebusierPhotoKind.LicenseFront, ArquebusierPhotoKind.LicenseBack],
            photos.Where(p => p.ArquebusierId == RegistrySeeder.AllPhotosArquebusier).Select(p => p.Kind).Order());
        Assert.DoesNotContain(photos, p => p.ArquebusierId == RegistrySeeder.NoPhotosArquebusier);
        Assert.Equal(photos.Select(p => p.ObjectKey).Order(StringComparer.Ordinal), await minio.ListKeysAsync(bucket, "registry/photos/"));

        using var client = minio.CreateClient();
        foreach (var photo in photos)
        {
            using var stored = await client.GetObjectAsync(bucket, photo.ObjectKey, TestContext.Current.CancellationToken);
            using var content = new MemoryStream();
            await stored.ResponseStream.CopyToAsync(content, TestContext.Current.CancellationToken);
            Assert.Equal("image/jpeg", stored.Headers.ContentType);
            Assert.DoesNotContain(TestImages.JpegMarkers(content.ToArray()), m => m is 0xE1 or 0xE2 or 0xED or 0xFE);
            Assert.Equal(photo.Kind == ArquebusierPhotoKind.Id ? (600, 800) : (1000, 630), (photo.Width, photo.Height));
        }
    }

    [Fact]
    public async Task Seeding_again_creates_no_photo_twice_and_restores_a_missing_image()
    {
        var bucket = $"seed-{Guid.NewGuid():N}";
        await using var host = await StartAsync(bucket);
        Assert.Equal(0, await SeedAsync(host));
        var keys = await minio.ListKeysAsync(bucket, "registry/photos/");
        using (var client = minio.CreateClient())
        {
            await client.DeleteObjectAsync(bucket, keys[0], TestContext.Current.CancellationToken);
        }

        Assert.Equal(0, await SeedAsync(host));

        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ArquebusierRegistryDbContext>();
        Assert.Equal(RegistrySeeder.PhotoCount, await db.Photos.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(keys, await minio.ListKeysAsync(bucket, "registry/photos/"));
    }

    [Fact]
    public async Task Outside_local_environments_the_seeders_own_photos_are_accepted()
    {
        await using var host = await StartAsync();
        Assert.Equal(0, await SeedAsync(host));
        await using var scope = host.Services.CreateAsyncScope();

        await NewSeeder(scope, host, "Staging").SeedAsync(TestContext.Current.CancellationToken);

        var db = scope.ServiceProvider.GetRequiredService<ArquebusierRegistryDbContext>();
        Assert.Equal(RegistrySeeder.PhotoCount, await db.Photos.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Outside_local_environments_a_database_with_a_real_photo_is_refused()
    {
        await using var host = await StartAsync();
        Assert.Equal(0, await SeedAsync(host));
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ArquebusierRegistryDbContext>();
        var owner = RegistrySeeder.NoPhotosArquebusier;
        db.Photos.Add(RegistryData.NewPhoto(owner, ArquebusierPhotoKind.Id));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => NewSeeder(scope, host, "Staging").SeedAsync(TestContext.Current.CancellationToken));

        Assert.Contains("not synthetic", error.Message, StringComparison.Ordinal);
    }

    private static RegistrySeeder NewSeeder(AsyncServiceScope scope, IdentityTestHost host, string environment) =>
        new(
            scope.ServiceProvider.GetRequiredService<ArquebusierRegistryDbContext>(),
            scope.ServiceProvider.GetRequiredService<ICatalogDirectory>(),
            scope.ServiceProvider.GetRequiredService<IObjectStorage>(),
            scope.ServiceProvider.GetRequiredService<IImageNormalizer>(),
            host.Time,
            new HostingEnvironment { EnvironmentName = environment },
            NullLogger<RegistrySeeder>.Instance);

    private Task<IdentityTestHost> StartAsync(string? bucket = null)
    {
        var settings = new Dictionary<string, string?> { [IdentitySeeder.PasswordKey] = SeedPassword, [IdentitySeeder.AuthenticatorKeyKey] = SeedKey };
        foreach (var (key, value) in bucket is null ? new Dictionary<string, string?>() : minio.SettingsFor(bucket))
        {
            settings[key] = value;
        }

        return IdentityTestHost.StartAsync(postgres, mailpit, settings);
    }

    private static Task<int> SeedAsync(IdentityTestHost host) =>
        SeedCommand.RunAsync(host.Services, new HostingEnvironment { EnvironmentName = Environments.Development }, TestContext.Current.CancellationToken);
}
