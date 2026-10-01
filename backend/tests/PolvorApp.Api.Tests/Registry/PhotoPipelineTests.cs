using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using PolvorApp.Api.Platform.Storage;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.SharedKernel.Storage;
using SkiaSharp;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Registry;

/// <summary>
/// Spec "Photo validation and processing" through the HTTP route, with the production rules: the
/// stored image is read back, so a skipped or misconfigured normalisation fails here (design D3, D5).
/// </summary>
[Collection(PostgresGroup.Name)]
public sealed class PhotoProcessingTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    /// <summary>APP1 (EXIF/XMP), APP2 (ICC), APP13 (IPTC), APP14 (Adobe) and comments.</summary>
    private static readonly byte[] MetadataMarkers = [0xE1, 0xE2, 0xED, 0xEE, 0xFE];

    private RegistryTestHost _registry = null!;
    private Guid _arquebusier;

    public async ValueTask InitializeAsync()
    {
        _registry = await RegistryTestHost.StartAsync(postgres, mailpit);
        _arquebusier = (await _registry.RegisterAsync(_registry.Own.Id)).GetProperty("id").GetGuid();
    }

    public async ValueTask DisposeAsync() => await _registry.DisposeAsync();

    [Fact]
    public async Task A_phone_photo_is_stored_upright_without_its_exif()
    {
        await UploadAsync("id", TestImages.JpegWithExif(600, 800, orientation: 6));

        var stored = await StoredAsync("id");

        Assert.DoesNotContain(TestImages.JpegMarkers(stored), MetadataMarkers.Contains);
        using var bitmap = SKBitmap.Decode(stored);
        Assert.Equal((600, 800), (bitmap.Width, bitmap.Height));
        Assert.True(TestImages.Near(bitmap.GetPixel(20, 20), TestImages.TopLeft));
        Assert.True(TestImages.Near(bitmap.GetPixel(580, 780), TestImages.BottomRight));
    }

    [Fact]
    public async Task Every_metadata_segment_and_trailing_data_is_dropped()
    {
        await UploadAsync("id", TestImages.JpegWithAllMetadata(600, 800));

        var stored = await StoredAsync("id");

        Assert.DoesNotContain(TestImages.JpegMarkers(stored), MetadataMarkers.Contains);
        Assert.Equal(-1, stored.AsSpan().IndexOf("Synthetic"u8));
        Assert.Equal(-1, stored.AsSpan().IndexOf("TRAILING"u8));
    }

    [Fact]
    public async Task A_large_id_photo_is_scaled_to_the_production_maximum()
    {
        var photo = await UploadAsync("id", TestImages.Jpeg(3000, 4000));

        using var bitmap = SKBitmap.Decode(await StoredAsync("id"));
        Assert.Equal((1200, 1600), (bitmap.Width, bitmap.Height));
        Assert.Equal((1200, 1600), (photo.GetProperty("width").GetInt32(), photo.GetProperty("height").GetInt32()));
    }

    [Fact]
    public async Task A_transparent_png_license_photo_is_stored_as_a_jpeg()
    {
        await UploadAsync("license-back", TestImages.Png(1200, 800, transparent: true));

        var stored = await StoredAsync("license-back");

        Assert.Equal([0xFF, 0xD8], stored[..2]);
        using var bitmap = SKBitmap.Decode(stored);
        Assert.Equal((1200, 800), (bitmap.Width, bitmap.Height));
    }

    [Theory]
    [InlineData(799, 600, "tooSmall")]
    [InlineData(2100, 1000, "aspectRatio")]
    public async Task License_photos_follow_their_own_rules(int width, int height, string reason)
    {
        using var response = await PhotoRequests.UploadAsync(_registry.FiringChief, _arquebusier, "license-front", TestImages.Jpeg(width, height));

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation");
        Assert.Equal(reason, (await ErrorsAsync(response))["file"]);
    }

    [Fact]
    public async Task A_license_photo_at_the_side_ratio_limit_is_accepted()
    {
        var photo = await UploadAsync("license-front", TestImages.Jpeg(1600, 800));

        Assert.Equal((1600, 800), (photo.GetProperty("width").GetInt32(), photo.GetProperty("height").GetInt32()));
    }

    private async Task<JsonElement> UploadAsync(string kind, byte[] image)
    {
        using var response = await PhotoRequests.UploadAsync(_registry.FiringChief, _arquebusier, kind, image);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadAsync<JsonElement>(response);
    }

    private async Task<byte[]> StoredAsync(string kind)
    {
        using var response = await _registry.Admin.GetAsync(
            new Uri($"/api/arquebusiers/{_arquebusier}/photos/{kind}", UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
    }
}

/// <summary>
/// Platform spec "Stored file cleanup" end to end: the real sweeper, the registry as owner, Postgres and
/// MinIO (design D2). Each test has its own bucket, so the remaining keys can be asserted exactly.
/// </summary>
[Collection(PostgresGroup.Name)]
public sealed class PhotoSweepTests(PostgresFixture postgres, MailpitFixture mailpit, MinioFixture minio) : IAsyncLifetime
{
    private const string Prefix = "registry/photos/";

    private RegistryTestHost _registry = null!;
    private string _bucket = string.Empty;

    public async ValueTask InitializeAsync()
    {
        _bucket = $"sweep-{Guid.NewGuid():N}";
        _registry = await RegistryTestHost.StartAsync(postgres, mailpit, settings: minio.SettingsFor(_bucket));
    }

    public async ValueTask DisposeAsync() => await _registry.DisposeAsync();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_sweep_erases_old_orphans_and_keeps_referenced_photos()
    {
        var referenced = await UploadAsync();
        var orphan = await PutOrphanAsync();

        await SweeperAt(DateTimeOffset.UtcNow.AddHours(2)).SweepAsync(Token);

        var keys = await minio.ListKeysAsync(_bucket, Prefix);
        Assert.Equal([$"{Prefix}{referenced:N}.jpg"], keys);
        Assert.DoesNotContain(orphan, keys);
    }

    [Fact]
    public async Task An_orphan_within_the_grace_period_is_kept()
    {
        var referenced = await UploadAsync();
        var orphan = await PutOrphanAsync();

        await SweeperAt(DateTimeOffset.UtcNow).SweepAsync(Token);

        Assert.Equal(
            new[] { $"{Prefix}{referenced:N}.jpg", orphan }.Order(StringComparer.Ordinal),
            (await minio.ListKeysAsync(_bucket, Prefix)).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_api_hosts_the_sweeper()
    {
        Assert.Single(_registry.Services.GetServices<IHostedService>().OfType<StoredObjectSweeper>());
    }

    private async Task<Guid> UploadAsync()
    {
        var arquebusier = (await _registry.RegisterAsync(_registry.Own.Id)).GetProperty("id").GetGuid();
        using var response = await PhotoRequests.UploadAsync(_registry.Admin, arquebusier, "id", TestImages.Jpeg(600, 800));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await ReadAsync<JsonElement>(response)).GetProperty("version").GetGuid();
    }

    /// <summary>An image left behind by an upload that never committed.</summary>
    private async Task<string> PutOrphanAsync()
    {
        var key = $"{Prefix}{Guid.CreateVersion7():N}.jpg";
        await _registry.Services.GetRequiredService<IObjectStorage>().PutAsync(key, TestImages.Jpeg(600, 800), "image/jpeg", Token);
        return key;
    }

    private StoredObjectSweeper SweeperAt(DateTimeOffset now) => new(
        _registry.Services.GetRequiredService<IServiceScopeFactory>(),
        _registry.Services.GetRequiredService<IObjectStorage>(),
        _registry.Services.GetRequiredService<IOptions<StorageOptions>>(),
        new FakeTimeProvider(now),
        NullLogger<StoredObjectSweeper>.Instance);
}
