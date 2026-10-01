using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using PolvorApp.Api.Platform.Storage;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.SharedKernel.Storage;

namespace PolvorApp.Api.Tests.Storage;

/// <summary>Spec: Private object storage, against a real MinIO (design D1).</summary>
public sealed class S3ObjectStorageTests(MinioFixture minio)
{
    private const string Secret = "unreachable-secret-sentinel";

    [Fact]
    public async Task An_object_round_trips_with_its_content_type()
    {
        using var storage = await CreateStorageAsync();
        var content = Encoding.UTF8.GetBytes("synthetic image bytes");

        await storage.PutAsync("registry/photos/one.jpg", content, "image/jpeg", Token);
        await using var stored = await storage.GetAsync("registry/photos/one.jpg", Token);

        Assert.NotNull(stored);
        Assert.Equal("image/jpeg", stored.ContentType);
        Assert.Equal(content.Length, stored.Length);
        using var read = new MemoryStream();
        await stored.Content.CopyToAsync(read, Token);
        Assert.Equal(content, read.ToArray());
    }

    [Fact]
    public async Task A_deleted_object_is_gone_and_deleting_it_again_succeeds()
    {
        using var storage = await CreateStorageAsync();
        await storage.PutAsync("registry/photos/two.jpg", new byte[] { 1, 2, 3 }, "image/jpeg", Token);

        await storage.DeleteAsync("registry/photos/two.jpg", Token);
        await storage.DeleteAsync("registry/photos/two.jpg", Token);

        Assert.Null(await storage.GetAsync("registry/photos/two.jpg", Token));
    }

    [Fact]
    public async Task A_missing_key_reads_as_null()
    {
        using var storage = await CreateStorageAsync();

        Assert.Null(await storage.GetAsync("registry/photos/missing.jpg", Token));
    }

    [Fact]
    public async Task Listing_pages_through_every_key_under_the_prefix_only()
    {
        using var storage = await CreateStorageAsync();
        const int count = 1005;
        await Parallel.ForEachAsync(Enumerable.Range(0, count), new ParallelOptions { MaxDegreeOfParallelism = 16, CancellationToken = Token }, async (i, ct) =>
            await storage.PutAsync($"registry/photos/{i:D4}.jpg", new byte[] { 1 }, "image/jpeg", ct));
        await storage.PutAsync("catalog/logos/outside.png", new byte[] { 1 }, "image/png", Token);

        var listed = new List<StoredObjectInfo>();
        await foreach (var item in storage.ListAsync("registry/photos/", Token))
        {
            listed.Add(item);
        }

        Assert.Equal(count, listed.Count);
        Assert.All(listed, item => Assert.StartsWith("registry/photos/", item.Key, StringComparison.Ordinal));
        Assert.All(listed, item => Assert.True(item.LastModified > DateTimeOffset.UtcNow.AddMinutes(-10)));
    }

    [Fact]
    public async Task An_unreachable_storage_raises_a_generic_exception_without_credentials()
    {
        // Nothing listens on port 1.
        using var storage = CreateStorage(new StorageOptions
        {
            ServiceUrl = new Uri("http://127.0.0.1:1"),
            Bucket = "polvorapp-test",
            AccessKey = "unreachable-access-key",
            SecretKey = Secret,
        });

        var put = await Assert.ThrowsAsync<StorageUnavailableException>(() =>
            storage.PutAsync("registry/photos/x.jpg", new byte[] { 1 }, "image/jpeg", Token));
        var get = await Assert.ThrowsAsync<StorageUnavailableException>(() => storage.GetAsync("registry/photos/x.jpg", Token));
        var delete = await Assert.ThrowsAsync<StorageUnavailableException>(() => storage.DeleteAsync("registry/photos/x.jpg", Token));

        Assert.All(new Exception[] { put, get, delete }, exception =>
        {
            Assert.DoesNotContain(Secret, exception.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("127.0.0.1", exception.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task A_storage_that_never_answers_times_out_as_unavailable()
    {
        // Accepts connections and never answers.
        using var silent = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        silent.Start();
        var port = ((IPEndPoint)silent.LocalEndpoint).Port;
        var options = Microsoft.Extensions.Options.Options.Create(new StorageOptions
        {
            ServiceUrl = new Uri($"http://127.0.0.1:{port}"),
            Bucket = "polvorapp-test",
            AccessKey = "silent-access-key",
            SecretKey = Secret,
        });
        using var storage = new S3ObjectStorage(options, NullLogger<S3ObjectStorage>.Instance, TimeSpan.FromSeconds(1));

        await Assert.ThrowsAsync<StorageUnavailableException>(() => storage.GetAsync("registry/photos/x.jpg", Token));
    }

    [Fact]
    public async Task A_caller_cancellation_is_not_an_outage()
    {
        using var storage = await CreateStorageAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            storage.PutAsync("registry/photos/x.jpg", new byte[] { 1 }, "image/jpeg", new CancellationToken(canceled: true)));
    }

    [Fact]
    public async Task A_missing_bucket_is_an_outage_not_a_missing_object()
    {
        using var storage = CreateStorage(Options($"missing-{Guid.NewGuid():N}"));

        await Assert.ThrowsAsync<StorageUnavailableException>(() => storage.GetAsync("registry/photos/x.jpg", Token));
    }

    [Theory]
    [InlineData("")]
    [InlineData("../registry/photos/x.jpg")]
    [InlineData("/registry/photos/x.jpg")]
    [InlineData("registry/photos/Pérez.jpg")]
    public async Task Keys_outside_the_convention_are_refused(string key)
    {
        using var storage = await CreateStorageAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => storage.PutAsync(key, new byte[] { 1 }, "image/jpeg", Token));
    }

    [Fact]
    public async Task Wrong_credentials_raise_a_generic_exception()
    {
        var bucket = await minio.CreateBucketAsync();
        using var storage = CreateStorage(new StorageOptions
        {
            ServiceUrl = new Uri(minio.ServiceUrl),
            Bucket = bucket,
            AccessKey = "wrong-access-key",
            SecretKey = Secret,
        });

        var exception = await Assert.ThrowsAsync<StorageUnavailableException>(() =>
            storage.PutAsync("registry/photos/x.jpg", new byte[] { 1 }, "image/jpeg", Token));

        Assert.DoesNotContain(Secret, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Anonymous_requests_cannot_read_objects()
    {
        var bucket = await minio.CreateBucketAsync();
        using var storage = CreateStorage(Options(bucket));
        await storage.PutAsync("registry/photos/private.jpg", new byte[] { 1, 2, 3 }, "image/jpeg", Token);
        using var anonymous = new HttpClient();

        var response = await anonymous.GetAsync(new Uri($"{minio.ServiceUrl}/{bucket}/registry/photos/private.jpg"), Token);

        // MinIO answers 403 AccessDenied, or 400 when the request carries no signature at all.
        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.Forbidden, HttpStatusCode.BadRequest });
        var body = await response.Content.ReadAsStringAsync(Token);
        Assert.Contains("<Error>", body, StringComparison.Ordinal);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private async Task<S3ObjectStorage> CreateStorageAsync() => CreateStorage(Options(await minio.CreateBucketAsync()));

    private StorageOptions Options(string bucket) => new()
    {
        ServiceUrl = new Uri(minio.ServiceUrl),
        Bucket = bucket,
        AccessKey = minio.SettingsFor(bucket)["Storage:AccessKey"],
        SecretKey = minio.SettingsFor(bucket)["Storage:SecretKey"],
    };

    private static S3ObjectStorage CreateStorage(StorageOptions options) =>
        new(Microsoft.Extensions.Options.Options.Create(options), NullLogger<S3ObjectStorage>.Instance);
}
