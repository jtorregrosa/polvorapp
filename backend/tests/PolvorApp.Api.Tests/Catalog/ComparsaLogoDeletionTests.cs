using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.FederationCatalog.Logos;
using PolvorApp.SharedKernel.Storage;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Catalog;

/// <summary>Spec "Deleting comparsas and weapon models": deleting a comparsa erases its logo (design D5).</summary>
public sealed class ComparsaLogoDeletionTests(PostgresFixture postgres, MailpitFixture mailpit, MinioFixture minio)
{
    [Fact]
    public async Task Deleting_an_unused_comparsa_erases_its_logo()
    {
        var bucket = await minio.CreateBucketAsync();
        await using var host = await RegistryTestHost.StartAsync(postgres, mailpit, settings: minio.SettingsFor(bucket));
        await UploadAsync(host);

        using var deletion = await host.Admin.DeleteAsync(new Uri($"/api/comparsas/{host.Other.Id}", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, deletion.StatusCode);
        Assert.Empty(await minio.ListKeysAsync(bucket, LogoStorage.Prefix));
        var entry = Assert.Single(await host.Host.AuditEntriesAsync("ComparsaDeleted"));
        using var data = JsonDocument.Parse(entry.Data!);
        Assert.True(data.RootElement.GetProperty("hadLogo").GetBoolean());
    }

    [Fact]
    public async Task A_comparsa_in_use_keeps_its_logo()
    {
        var bucket = await minio.CreateBucketAsync();
        var inUse = new FakeCatalogUsage();
        await using var host = await RegistryTestHost.StartAsync(
            postgres, mailpit, services => services.AddSingleton<ICatalogUsage>(inUse), minio.SettingsFor(bucket));
        var version = await UploadAsync(host);
        inUse.ComparsasInUse.Add(host.Other.Id);

        using var deletion = await host.Admin.DeleteAsync(new Uri($"/api/comparsas/{host.Other.Id}", UriKind.Relative), TestContext.Current.CancellationToken);

        await AssertProblemAsync(deletion, HttpStatusCode.Conflict, "comparsas.inUse");
        Assert.Equal([LogoStorage.KeyFor(version)], await minio.ListKeysAsync(bucket, LogoStorage.Prefix));
    }

    [Fact]
    public async Task The_deletion_succeeds_when_the_logo_cannot_be_erased()
    {
        var bucket = await minio.CreateBucketAsync();
        await using var host = await RegistryTestHost.StartAsync(
            postgres, mailpit,
            services => services.AddSingleton<IObjectStorage>(provider => new UndeletableStorage(provider.GetRequiredService<PolvorApp.Api.Platform.Storage.S3ObjectStorage>())),
            minio.SettingsFor(bucket));
        var version = await UploadAsync(host);

        using var deletion = await host.Admin.DeleteAsync(new Uri($"/api/comparsas/{host.Other.Id}", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, deletion.StatusCode);
        // Left for the orphan sweep.
        Assert.Equal([LogoStorage.KeyFor(version)], await minio.ListKeysAsync(bucket, LogoStorage.Prefix));
    }

    [Fact]
    public async Task A_replaced_logo_that_cannot_be_erased_is_erased_by_the_cleanup()
    {
        var bucket = await minio.CreateBucketAsync();
        await using var host = await RegistryTestHost.StartAsync(
            postgres, mailpit,
            services => services.AddSingleton<IObjectStorage>(provider => new UndeletableStorage(provider.GetRequiredService<PolvorApp.Api.Platform.Storage.S3ObjectStorage>())),
            minio.SettingsFor(bucket));
        var first = await UploadAsync(host);

        var second = await UploadAsync(host);
        Assert.Equal(
            new[] { LogoStorage.KeyFor(first), LogoStorage.KeyFor(second) }.Order(StringComparer.Ordinal),
            (await minio.ListKeysAsync(bucket, LogoStorage.Prefix)).Order(StringComparer.Ordinal));

        // The next cleanup run, with a working storage, more than an hour later.
        var real = host.Services.GetRequiredService<PolvorApp.Api.Platform.Storage.S3ObjectStorage>();
        await new PolvorApp.Api.Platform.Storage.StoredObjectSweeper(
            host.Services.GetRequiredService<IServiceScopeFactory>(),
            real,
            host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<PolvorApp.Api.Platform.Storage.StorageOptions>>(),
            new Microsoft.Extensions.Time.Testing.FakeTimeProvider(DateTimeOffset.UtcNow.AddHours(2)),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<PolvorApp.Api.Platform.Storage.StoredObjectSweeper>.Instance)
            .SweepAsync(TestContext.Current.CancellationToken);

        Assert.Equal([LogoStorage.KeyFor(second)], await minio.ListKeysAsync(bucket, LogoStorage.Prefix));
    }

    private static async Task<Guid> UploadAsync(RegistryTestHost host)
    {
        using var response = await LogoRequests.UploadAsync(host.Admin, host.Other.Id, TestImages.Png(800, 400));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await ReadAsync<JsonElement>(response)).GetProperty("version").GetGuid();
    }

    /// <summary>Stores and reads normally, but never deletes.</summary>
    private sealed class UndeletableStorage(IObjectStorage inner) : IObjectStorage
    {
        public Task PutAsync(string key, ReadOnlyMemory<byte> content, string contentType, CancellationToken cancellationToken) =>
            inner.PutAsync(key, content, contentType, cancellationToken);

        public Task<StoredObject?> GetAsync(string key, CancellationToken cancellationToken) => inner.GetAsync(key, cancellationToken);

        public Task DeleteAsync(string key, CancellationToken cancellationToken) => throw new StorageUnavailableException();

        public IAsyncEnumerable<StoredObjectInfo> ListAsync(string prefix, CancellationToken cancellationToken) => inner.ListAsync(prefix, cancellationToken);
    }
}
