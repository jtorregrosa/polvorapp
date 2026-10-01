using System.Net;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using PolvorApp.SharedKernel.Storage;

namespace PolvorApp.Api.Platform.Storage;

/// <summary>
/// <see cref="IObjectStorage"/> over the S3 API, so MinIO and any S3-compatible provider are
/// interchangeable (ADR-0005, design D1). Every library failure becomes a
/// <see cref="StorageUnavailableException"/> with a generic message; the caller's cancellation is
/// passed through.
/// </summary>
internal sealed partial class S3ObjectStorage : IObjectStorage, IDisposable
{
    private const int MaxRetries = 2;

    /// <summary>Answers that only an operator can fix: wrong credentials, bucket or signing settings.</summary>
    private static readonly HashSet<string> ConfigurationErrors =
        ["InvalidAccessKeyId", "SignatureDoesNotMatch", "AccessDenied", "NoSuchBucket", "AuthorizationHeaderMalformed"];

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    private readonly AmazonS3Client _client;
    private readonly StorageOptions _options;
    private readonly ILogger<S3ObjectStorage> _logger;

    public S3ObjectStorage(IOptions<StorageOptions> options, ILogger<S3ObjectStorage> logger)
        : this(options, logger, RequestTimeout)
    {
    }

    /// <summary>With a shorter <paramref name="timeout"/>, for tests of a storage that never answers.</summary>
    internal S3ObjectStorage(IOptions<StorageOptions> options, ILogger<S3ObjectStorage> logger, TimeSpan timeout)
    {
        _options = options.Value;
        _logger = logger;
        _client = new AmazonS3Client(_options.AccessKey, _options.SecretKey, CreateConfig(_options, timeout));
    }

    public string Bucket => _options.Bucket!;

    /// <summary>The client, for the platform's bucket bootstrap and health check.</summary>
    internal IAmazonS3 Client => _client;

    public async Task PutAsync(string key, ReadOnlyMemory<byte> content, string contentType, CancellationToken cancellationToken)
    {
        EnsureValidKey(key);
        using var stream = MemoryMarshal.TryGetArray(content, out var segment)
            ? new MemoryStream(segment.Array!, segment.Offset, segment.Count, writable: false)
            : new MemoryStream(content.ToArray(), writable: false);
        var request = new PutObjectRequest
        {
            BucketName = Bucket,
            Key = key,
            InputStream = stream,
            ContentType = contentType,
            AutoCloseStream = false,
        };
        if (_options.ServerSideEncryption == StorageEncryption.Aes256)
        {
            request.ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256;
        }

        await RunAsync("put", key, () => _client.PutObjectAsync(request, cancellationToken), cancellationToken);
        LogStored(_logger, key, content.Length);
    }

    public async Task<StoredObject?> GetAsync(string key, CancellationToken cancellationToken)
    {
        EnsureValidKey(key);
        var response = await RunAsync("get", key, async () =>
        {
            try
            {
                return await _client.GetObjectAsync(Bucket, key, cancellationToken);
            }
            catch (AmazonS3Exception exception) when (exception is { StatusCode: HttpStatusCode.NotFound, ErrorCode: "NoSuchKey" })
            {
                // A missing key is an answer, not a failure.
                return null;
            }
        }, cancellationToken);
        return response is null ? null : new StoredObject(response.ResponseStream, response.Headers.ContentType, response.Headers.ContentLength);
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        EnsureValidKey(key);

        // S3 answers 204 for a missing key too, so a repeated delete succeeds.
        await RunAsync("delete", key, () => _client.DeleteObjectAsync(Bucket, key, cancellationToken), cancellationToken);
        LogDeleted(_logger, key);
    }

    public async IAsyncEnumerable<StoredObjectInfo> ListAsync(string prefix, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var request = new ListObjectsV2Request { BucketName = Bucket, Prefix = prefix };
        ListObjectsV2Response page;
        do
        {
            page = await RunAsync("list", prefix, () => _client.ListObjectsV2Async(request, cancellationToken), cancellationToken);
            foreach (var item in page.S3Objects ?? [])
            {
                yield return new StoredObjectInfo(item.Key, LastModifiedOf(item));
            }

            if (page.IsTruncated == true && string.IsNullOrEmpty(page.NextContinuationToken))
            {
                // Re-sending the same request would list the first page forever.
                throw new StorageUnavailableException("The object storage returned a truncated listing without a continuation token.");
            }

            request.ContinuationToken = page.NextContinuationToken;
        }
        while (page.IsTruncated == true);
    }

    public void Dispose() => _client.Dispose();

    /// <summary>Path-style, signed for the configured region, without the checksums some providers reject (design D1).</summary>
    internal static AmazonS3Config CreateConfig(StorageOptions options, TimeSpan timeout) => new()
    {
        ServiceURL = options.ServiceUrl!.AbsoluteUri,
        ForcePathStyle = options.ForcePathStyle,
        AuthenticationRegion = options.Region,
        RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
        ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
        MaxErrorRetry = MaxRetries,
        Timeout = timeout,
    };

    /// <summary>
    /// When the object was written, in UTC. A listing without the date reads as "just written", so
    /// the sweep's grace period protects the object instead of treating it as ancient (design D2).
    /// </summary>
    private static DateTimeOffset LastModifiedOf(S3Object item) => item.LastModified switch
    {
        { Kind: DateTimeKind.Unspecified } modified => new DateTimeOffset(DateTime.SpecifyKind(modified, DateTimeKind.Utc)),
        { } modified => new DateTimeOffset(modified.ToUniversalTime()),
        null => DateTimeOffset.MaxValue,
    };

    /// <summary>Keys are generated by the server (modules README); anything else is a programming error.</summary>
    private static void EnsureValidKey(string key)
    {
        if (!ValidKey().IsMatch(key))
        {
            throw new ArgumentException("The object key does not follow the <module>/<collection>/<name>.<ext> convention.", nameof(key));
        }
    }

    private async Task<T> RunAsync<T>(string operation, string key, Func<Task<T>> call, CancellationToken cancellationToken)
    {
        try
        {
            return await call();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is AmazonServiceException or AmazonClientException or HttpRequestException or IOException or OperationCanceledException or TimeoutException)
        {
            switch (exception)
            {
                case AmazonServiceException service when ConfigurationErrors.Contains(service.ErrorCode ?? string.Empty):
                    LogMisconfigured(_logger, operation, service.ErrorCode, service.StatusCode, exception);
                    break;
                default:
                    LogFailed(_logger, operation, key, exception);
                    break;
            }

            throw new StorageUnavailableException("The object storage is unavailable.", exception);
        }
    }

    [GeneratedRegex(@"^[a-z0-9-]+/[a-z0-9-]+/[A-Za-z0-9-]+\.[a-z0-9]+$", RegexOptions.CultureInvariant)]
    private static partial Regex ValidKey();

    [LoggerMessage(Level = LogLevel.Error, Message = "Object storage {Operation} refused with {ErrorCode} ({Status}): check the Storage settings")]
    private static partial void LogMisconfigured(ILogger logger, string operation, string? errorCode, HttpStatusCode status, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Stored object {Key} ({Bytes} bytes).")]
    private static partial void LogStored(ILogger logger, string key, int bytes);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Deleted object {Key}.")]
    private static partial void LogDeleted(ILogger logger, string key);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Object storage {Operation} failed for {Key}.")]
    private static partial void LogFailed(ILogger logger, string operation, string key, Exception exception);
}
