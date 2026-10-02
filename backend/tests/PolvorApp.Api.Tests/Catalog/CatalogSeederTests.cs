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
using PolvorApp.FederationCatalog.Logos;
using PolvorApp.FederationCatalog.Persistence;
using PolvorApp.FederationCatalog.Seeding;
using PolvorApp.FederationCatalog.WeaponModels;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.IdentityAccess.Endpoints;
using PolvorApp.IdentityAccess.Security;
using PolvorApp.IdentityAccess.Users;
using PolvorApp.SharedKernel.Images;
using PolvorApp.SharedKernel.Storage;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Catalog;

/// <summary>Spec "Synthetic catalogue data" (SEC-11): fictional, deterministic, safe to run again.</summary>
public sealed class CatalogSeederTests(PostgresFixture postgres, MailpitFixture mailpit, MinioFixture minio)
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
    public async Task Most_seeded_comparsas_get_a_generated_logo_that_passes_the_logo_rules()
    {
        var bucket = await minio.CreateBucketAsync();
        await using var host = await StartAsync(minio.SettingsFor(bucket));

        Assert.Equal(0, await SeedAsync(host));
        Assert.Equal(0, await SeedAsync(host));

        await using var scope = host.Services.CreateAsyncScope();
        var comparsas = await scope.ServiceProvider.GetRequiredService<FederationCatalogDbContext>().Comparsas.AsNoTracking()
            .ToDictionaryAsync(c => c.Id, TestContext.Current.CancellationToken);
        Assert.Null(comparsas[CatalogSeeder.Sur].Logo);
        var logos = comparsas.Values.Where(c => c.Logo is not null).Select(c => c.Logo!).ToList();
        Assert.Equal(CatalogSeeder.Logos.Select(l => l.LogoId).Order(), logos.Select(l => l.Id).Order());
        Assert.Equal(logos.Select(l => LogoStorage.KeyFor(l.Id)).Order(StringComparer.Ordinal), await minio.ListKeysAsync(bucket, LogoStorage.Prefix));

        var storage = scope.ServiceProvider.GetRequiredService<IObjectStorage>();
        var normalizer = scope.ServiceProvider.GetRequiredService<IImageNormalizer>();
        var darkest = 255.0;
        foreach (var logo in logos)
        {
            var png = await ReadAsync(storage, logo.ObjectKey);
            // Stored images already pass the rules: normalising them again is accepted unchanged in size.
            using var input = new MemoryStream(png);
            var again = Assert.IsType<NormalizedImage>(await normalizer.NormalizeAsync(input, LogoStorage.Rules, TestContext.Current.CancellationToken));
            Assert.Equal((logo.Width, logo.Height), (again.Width, again.Height));
            using var bitmap = SkiaSharp.SKBitmap.Decode(png);
            Assert.Equal(0, bitmap.GetPixel(0, 0).Alpha); // transparent background
            darkest = Math.Min(darkest, AverageOpaqueLuminance(bitmap));
        }

        // At least one emblem needs the light tile in the dark theme.
        Assert.True(darkest < 40, $"The darkest seeded logo averages {darkest:F0}.");
    }

    [Fact]
    public async Task A_rerun_restores_a_seeded_logo_whose_image_went_missing()
    {
        var bucket = await minio.CreateBucketAsync();
        await using var host = await StartAsync(minio.SettingsFor(bucket));
        Assert.Equal(0, await SeedAsync(host));
        await using var scope = host.Services.CreateAsyncScope();
        var before = (await NorteAsync(host)).Logo!;
        var storage = scope.ServiceProvider.GetRequiredService<IObjectStorage>();
        await storage.DeleteAsync(before.ObjectKey, TestContext.Current.CancellationToken);

        Assert.Equal(0, await SeedAsync(host));

        Assert.NotEmpty(await ReadAsync(storage, before.ObjectKey));
        var after = (await NorteAsync(host)).Logo!;
        Assert.Equal((before.Id, before.UploadedAt), (after.Id, after.UploadedAt));
        Assert.Equal(3, (await minio.ListKeysAsync(bucket, LogoStorage.Prefix)).Count);
    }

    [Fact]
    public async Task Locally_a_logo_an_Admin_replaced_is_kept_and_the_seeded_one_is_not_restored()
    {
        var bucket = await minio.CreateBucketAsync();
        await using var host = await StartAsync(minio.SettingsFor(bucket));
        Assert.Equal(0, await SeedAsync(host));
        var seededKey = (await NorteAsync(host)).Logo!.ObjectKey;
        var replacement = await ReplaceNorteLogoAsync(host);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            // As after the upload's erasure of the replaced image.
            await scope.ServiceProvider.GetRequiredService<IObjectStorage>().DeleteAsync(seededKey, TestContext.Current.CancellationToken);
        }

        Assert.Equal(0, await SeedAsync(host));

        Assert.Equal(replacement.Id, (await NorteAsync(host)).Logo!.Id);
        Assert.DoesNotContain(seededKey, await minio.ListKeysAsync(bucket, LogoStorage.Prefix));
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
    [InlineData("logo")]
    public async Task Outside_local_environments_a_database_with_real_catalogue_data_is_refused_before_writing(string real)
    {
        var bucket = await minio.CreateBucketAsync();
        await using var host = await StartAsync(minio.SettingsFor(bucket));
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FederationCatalogDbContext>();
        var staging = new CatalogSeeder(
            db,
            scope.ServiceProvider.GetRequiredService<IObjectStorage>(),
            scope.ServiceProvider.GetRequiredService<IImageNormalizer>(),
            host.Time,
            new HostingEnvironment { EnvironmentName = "Staging" },
            NullLogger<CatalogSeeder>.Instance);
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
            case "logo":
                await ReplaceNorteLogoAsync(host);
                break;
            default:
                await host.AssignAsync(CatalogSeeder.Sur, Guid.CreateVersion7());
                break;
        }

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.ChangeTracker.Clear();
        var keys = await minio.ListKeysAsync(bucket, LogoStorage.Prefix);
        await scope.ServiceProvider.GetRequiredService<IObjectStorage>().DeleteAsync(LogoStorage.KeyFor(CatalogSeeder.Logos[1].LogoId), TestContext.Current.CancellationToken);
        var keysBefore = await minio.ListKeysAsync(bucket, LogoStorage.Prefix);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => staging.SeedAsync(TestContext.Current.CancellationToken));

        Assert.Contains("not synthetic", error.Message, StringComparison.Ordinal);
        Assert.Equal(comparsas, await db.Comparsas.CountAsync(TestContext.Current.CancellationToken));
        // Nothing was written to the storage either: the missing seeded image was not restored.
        Assert.Equal(keys.Count - 1, keysBefore.Count);
        Assert.Equal(keysBefore, await minio.ListKeysAsync(bucket, LogoStorage.Prefix));
    }

    private static async Task<Comparsa> NorteAsync(IdentityTestHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<FederationCatalogDbContext>().Comparsas.AsNoTracking()
            .SingleAsync(c => c.Id == CatalogSeeder.Norte, TestContext.Current.CancellationToken);
    }

    /// <summary>Gives Norte a logo that the seeder did not create, as an Admin's upload would.</summary>
    private static async Task<ComparsaLogo> ReplaceNorteLogoAsync(IdentityTestHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var id = Guid.CreateVersion7();
        var logo = new ComparsaLogo
        {
            Id = id,
            ObjectKey = LogoStorage.KeyFor(id),
            Width = 300,
            Height = 300,
            SizeBytes = 1024,
            UploadedAt = host.Time.GetUtcNow(),
        };
        await scope.ServiceProvider.GetRequiredService<IObjectStorage>()
            .PutAsync(logo.ObjectKey, TestImages.Png(300, 300), LogoStorage.ContentType, TestContext.Current.CancellationToken);
        var db = scope.ServiceProvider.GetRequiredService<FederationCatalogDbContext>();
        var norte = await db.Comparsas.SingleAsync(c => c.Id == CatalogSeeder.Norte, TestContext.Current.CancellationToken);
        norte.Logo = logo;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return logo;
    }

    private Task<IdentityTestHost> StartAsync(IReadOnlyDictionary<string, string?>? storage = null)
    {
        var settings = new Dictionary<string, string?> { [IdentitySeeder.PasswordKey] = SeedPassword, [IdentitySeeder.AuthenticatorKeyKey] = SeedKey };
        foreach (var (key, value) in storage ?? new Dictionary<string, string?>())
        {
            settings[key] = value;
        }

        return IdentityTestHost.StartAsync(postgres, mailpit, settings);
    }

    private static async Task<byte[]> ReadAsync(IObjectStorage storage, string key)
    {
        await using var stored = await storage.GetAsync(key, TestContext.Current.CancellationToken);
        Assert.NotNull(stored);
        using var buffer = new MemoryStream();
        await stored.Content.CopyToAsync(buffer, TestContext.Current.CancellationToken);
        return buffer.ToArray();
    }

    /// <summary>Mean luminance (0–255) of the opaque pixels.</summary>
    private static double AverageOpaqueLuminance(SkiaSharp.SKBitmap bitmap)
    {
        var (sum, count) = (0.0, 0);
        for (var y = 0; y < bitmap.Height; y += 4)
        {
            for (var x = 0; x < bitmap.Width; x += 4)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.Alpha == 255)
                {
                    sum += (0.2126 * pixel.Red) + (0.7152 * pixel.Green) + (0.0722 * pixel.Blue);
                    count++;
                }
            }
        }

        return count == 0 ? 255 : sum / count;
    }

    private static Task<int> SeedAsync(IdentityTestHost host) =>
        SeedCommand.RunAsync(host.Services, new HostingEnvironment { EnvironmentName = Environments.Development }, TestContext.Current.CancellationToken);
}
