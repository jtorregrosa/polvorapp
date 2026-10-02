using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using PolvorApp.Api.Platform.Storage;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.FederationCatalog.Logos;
using PolvorApp.SharedKernel.Storage;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Catalog;

/// <summary>
/// Spec "Comparsa logos" (cleanup) with the platform's "Stored file cleanup": the real sweeper, the
/// catalogue as owner of <c>catalog/logos/</c>, Postgres and MinIO (design D5). Each test has its own bucket.
/// </summary>
[Collection(PostgresGroup.Name)]
public sealed class ComparsaLogoSweepTests(PostgresFixture postgres, MailpitFixture mailpit, MinioFixture minio) : IAsyncLifetime
{
    private RegistryTestHost _host = null!;
    private string _bucket = string.Empty;

    public async ValueTask InitializeAsync()
    {
        _bucket = $"sweep-{Guid.NewGuid():N}";
        _host = await RegistryTestHost.StartAsync(postgres, mailpit, settings: minio.SettingsFor(_bucket));
    }

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_catalogue_reports_only_the_logos_it_references()
    {
        var referenced = LogoStorage.KeyFor(await UploadAsync());
        var orphan = LogoStorage.KeyFor(Guid.CreateVersion7());

        await using var scope = _host.Services.CreateAsyncScope();
        var owner = Assert.Single(scope.ServiceProvider.GetServices<IStoredObjectOwner>(), o => o.Prefix == LogoStorage.Prefix);
        var found = await owner.FilterReferencedAsync([referenced, referenced.ToUpperInvariant(), orphan], Token);

        Assert.Equal([referenced], found);
    }

    [Fact]
    public async Task The_sweep_erases_old_orphan_logos_and_keeps_referenced_ones()
    {
        var referenced = LogoStorage.KeyFor(await UploadAsync());
        var orphan = await PutOrphanAsync();

        await SweeperAt(DateTimeOffset.UtcNow.AddHours(2)).SweepAsync(Token);

        var keys = await minio.ListKeysAsync(_bucket, LogoStorage.Prefix);
        Assert.Equal([referenced], keys);
        Assert.DoesNotContain(orphan, keys);
    }

    private async Task<Guid> UploadAsync()
    {
        using var response = await LogoRequests.UploadAsync(_host.Admin, _host.Own.Id, TestImages.Png(800, 400));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await ReadAsync<JsonElement>(response)).GetProperty("version").GetGuid();
    }

    /// <summary>An image left behind by an upload that never committed.</summary>
    private async Task<string> PutOrphanAsync()
    {
        var key = LogoStorage.KeyFor(Guid.CreateVersion7());
        await _host.Services.GetRequiredService<IObjectStorage>().PutAsync(key, TestImages.Png(300, 300), LogoStorage.ContentType, Token);
        return key;
    }

    private StoredObjectSweeper SweeperAt(DateTimeOffset now) => new(
        _host.Services.GetRequiredService<IServiceScopeFactory>(),
        _host.Services.GetRequiredService<IObjectStorage>(),
        _host.Services.GetRequiredService<IOptions<StorageOptions>>(),
        new FakeTimeProvider(now),
        NullLogger<StoredObjectSweeper>.Instance);
}
