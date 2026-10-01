using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PolvorApp.Api.Platform.Database;
using PolvorApp.Api.Platform.Storage;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.SharedKernel.Storage;

namespace PolvorApp.Api.Tests.Storage;

/// <summary>Spec: Private object storage ("Bucket created by migrate"), design D1.</summary>
[Collection(PostgresGroup.Name)]
public sealed class StorageBootstrapTests(PostgresFixture postgres, MinioFixture minio)
{
    [Fact]
    public async Task Migrate_creates_a_missing_bucket_and_a_rerun_leaves_it_unchanged()
    {
        var bucket = $"bootstrap-{Guid.NewGuid():N}";
        await using var factory = new ApiFactory(await postgres.CreateDatabaseAsync(), settings: minio.SettingsFor(bucket));

        Assert.Equal(0, await MigrateCommand.RunAsync(factory.Services, Token));
        using var client = minio.CreateClient();
        await client.PutObjectAsync(new PutObjectRequest { BucketName = bucket, Key = "registry/photos/kept.jpg", ContentBody = "x" }, Token);
        Assert.Equal(0, await MigrateCommand.RunAsync(factory.Services, Token));

        Assert.Equal(["registry/photos/kept.jpg"], await minio.ListKeysAsync(bucket));
    }

    [Fact]
    public async Task An_existing_bucket_passes_with_a_key_that_cannot_create_buckets()
    {
        var bucket = await minio.CreateBucketAsync();
        using var client = new NoCreateClient(minio);
        var bootstrapper = new StorageBootstrapper(client, bucket, TimeProvider.System, NullLogger<StorageBootstrapper>.Instance);

        await bootstrapper.EnsureBucketAsync(Token);

        Assert.Equal(0, client.CreateAttempts);
    }

    [Fact]
    public async Task The_bootstrap_retries_while_the_storage_is_starting()
    {
        var bucket = await minio.CreateBucketAsync();
        using var client = new StartingClient(minio, failures: 2);
        var bootstrapper = new StorageBootstrapper(client, bucket, TimeProvider.System, NullLogger<StorageBootstrapper>.Instance, retryDelay: TimeSpan.FromMilliseconds(10));

        await bootstrapper.EnsureBucketAsync(Token);

        Assert.Equal(3, client.HeadAttempts);
    }

    [Fact]
    public async Task The_bootstrap_gives_up_after_its_deadline()
    {
        using var client = new StartingClient(minio, failures: int.MaxValue);
        var bootstrapper = new StorageBootstrapper(
            client, "never-reachable", TimeProvider.System, NullLogger<StorageBootstrapper>.Instance,
            retryDelay: TimeSpan.FromMilliseconds(10), maxWait: TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAsync<StorageUnavailableException>(() => bootstrapper.EnsureBucketAsync(Token));
    }

    [Fact]
    public async Task A_refused_key_fails_at_once_without_waiting()
    {
        using var client = new ForbiddenClient(minio);
        var bootstrapper = new StorageBootstrapper(
            client, "any-bucket", TimeProvider.System, NullLogger<StorageBootstrapper>.Instance,
            retryDelay: TimeSpan.FromSeconds(5), maxWait: TimeSpan.FromMinutes(1));
        var started = DateTimeOffset.UtcNow;

        await Assert.ThrowsAsync<StorageUnavailableException>(() => bootstrapper.EnsureBucketAsync(Token));

        Assert.True(DateTimeOffset.UtcNow - started < TimeSpan.FromSeconds(5));
        Assert.Equal(1, client.HeadAttempts);
    }

    [Fact]
    public async Task Migrate_fails_with_a_critical_log_when_the_storage_is_unreachable()
    {
        await using var factory = new ApiFactory(await postgres.CreateDatabaseAsync(), settings: new Dictionary<string, string?>
        {
            ["Storage:ServiceUrl"] = "http://127.0.0.1:1",
            ["Storage:BootstrapMaxWaitSeconds"] = "1",
        });

        var exitCode = await MigrateCommand.RunAsync(factory.Services, Token);

        Assert.Equal(1, exitCode);
        Assert.Contains(factory.Logs.Entries, e => e.Level == LogLevel.Critical && e.Message.Contains("storage", StringComparison.OrdinalIgnoreCase));
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>A key allowed to read the bucket but not to create buckets.</summary>
    private sealed class NoCreateClient(MinioFixture minio) : AmazonS3Client(
        minio.SettingsFor("unused")["Storage:AccessKey"], minio.SettingsFor("unused")["Storage:SecretKey"],
        new AmazonS3Config { ServiceURL = minio.ServiceUrl, ForcePathStyle = true, AuthenticationRegion = "us-east-1" })
    {
        public int CreateAttempts { get; private set; }

        public override Task<PutBucketResponse> PutBucketAsync(PutBucketRequest request, CancellationToken cancellationToken = default)
        {
            CreateAttempts++;
            throw new AmazonS3Exception("Access Denied.") { StatusCode = HttpStatusCode.Forbidden, ErrorCode = "AccessDenied" };
        }
    }

    /// <summary>A key the storage refuses outright (403).</summary>
    private sealed class ForbiddenClient(MinioFixture minio) : AmazonS3Client(
        minio.SettingsFor("unused")["Storage:AccessKey"], minio.SettingsFor("unused")["Storage:SecretKey"],
        new AmazonS3Config { ServiceURL = minio.ServiceUrl, ForcePathStyle = true, AuthenticationRegion = "us-east-1" })
    {
        public int HeadAttempts { get; private set; }

        public override Task<HeadBucketResponse> HeadBucketAsync(HeadBucketRequest request, CancellationToken cancellationToken = default)
        {
            HeadAttempts++;
            throw new AmazonS3Exception("Forbidden.") { StatusCode = HttpStatusCode.Forbidden, ErrorCode = "AccessDenied" };
        }
    }

    /// <summary>A storage that refuses connections for the first <c>failures</c> requests.</summary>
    private sealed class StartingClient(MinioFixture minio, int failures) : AmazonS3Client(
        minio.SettingsFor("unused")["Storage:AccessKey"], minio.SettingsFor("unused")["Storage:SecretKey"],
        new AmazonS3Config { ServiceURL = minio.ServiceUrl, ForcePathStyle = true, AuthenticationRegion = "us-east-1" })
    {
        public int HeadAttempts { get; private set; }

        public override Task<HeadBucketResponse> HeadBucketAsync(HeadBucketRequest request, CancellationToken cancellationToken = default)
        {
            HeadAttempts++;
            return HeadAttempts <= failures
                ? throw new AmazonClientException("Connection refused.", new HttpRequestException())
                : base.HeadBucketAsync(request, cancellationToken);
        }
    }
}
