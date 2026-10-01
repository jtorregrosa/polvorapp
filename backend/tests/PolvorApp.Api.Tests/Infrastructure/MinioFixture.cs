using Amazon.S3;
using Amazon.S3.Model;
using Testcontainers.Minio;

[assembly: AssemblyFixture(typeof(PolvorApp.Api.Tests.Infrastructure.MinioFixture))]

namespace PolvorApp.Api.Tests.Infrastructure;

/// <summary>
/// A MinIO container shared by the test assembly, on the same image as compose. Every
/// <see cref="ApiFactory"/> points at its <see cref="SharedBucket"/> unless a test overrides the
/// storage settings; tests that need isolation create a bucket of their own.
/// </summary>
public sealed class MinioFixture : IAsyncLifetime
{
    public const string Image = "cgr.dev/chainguard/minio@sha256:4692462f35d97d7e82c30371d82f057703c5d9489bcae726010594c812f2d285";
    public const string SharedBucket = "polvorapp-test";
    private const string Username = "polvorapp";
    private const string Password = "test-only-minio-password";

    private readonly MinioContainer _container = new MinioBuilder(Image)
        .WithUsername(Username)
        .WithPassword(Password)
        .Build();

    /// <summary>Storage settings of the running container, read by <see cref="ApiFactory"/>; null before it starts.</summary>
    public static IReadOnlyDictionary<string, string?>? SharedSettings { get; private set; }

    public string ServiceUrl => _container.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        using (var client = CreateClient())
        {
            await client.PutBucketAsync(new PutBucketRequest { BucketName = SharedBucket });
        }

        SharedSettings = SettingsFor(SharedBucket);
    }

    public async ValueTask DisposeAsync()
    {
        SharedSettings = null;
        await _container.DisposeAsync();
    }

    /// <summary>Storage settings for an <see cref="ApiFactory"/> that uses <paramref name="bucket"/> in this container.</summary>
    public IReadOnlyDictionary<string, string?> SettingsFor(string bucket) => new Dictionary<string, string?>
    {
        ["Storage:ServiceUrl"] = ServiceUrl,
        ["Storage:Bucket"] = bucket,
        ["Storage:AccessKey"] = Username,
        ["Storage:SecretKey"] = Password,
    };

    /// <summary>An administrative client for test setup and assertions.</summary>
    public AmazonS3Client CreateClient() => new(Username, Password, new AmazonS3Config
    {
        ServiceURL = ServiceUrl,
        ForcePathStyle = true,
        AuthenticationRegion = "us-east-1",
    });

    /// <summary>Creates an empty bucket with a unique name and returns the name.</summary>
    public async Task<string> CreateBucketAsync()
    {
        var bucket = $"test-{Guid.NewGuid():N}";
        using var client = CreateClient();
        await client.PutBucketAsync(new PutBucketRequest { BucketName = bucket }, TestContext.Current.CancellationToken);
        return bucket;
    }

    /// <summary>The keys stored in <paramref name="bucket"/> under <paramref name="prefix"/>.</summary>
    public async Task<IReadOnlyList<string>> ListKeysAsync(string bucket, string prefix = "")
    {
        using var client = CreateClient();
        var keys = new List<string>();
        var request = new ListObjectsV2Request { BucketName = bucket, Prefix = prefix };
        ListObjectsV2Response response;
        do
        {
            response = await client.ListObjectsV2Async(request, TestContext.Current.CancellationToken);
            keys.AddRange((response.S3Objects ?? []).Select(o => o.Key));
            request.ContinuationToken = response.NextContinuationToken;
        }
        while (response.IsTruncated == true);

        return keys;
    }
}
