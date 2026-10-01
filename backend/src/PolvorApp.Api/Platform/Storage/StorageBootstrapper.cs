using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using PolvorApp.SharedKernel.Storage;

namespace PolvorApp.Api.Platform.Storage;

/// <summary>
/// Makes sure the configured bucket exists, for the <c>migrate</c> command (spec: Private object
/// storage, design D1). It creates the bucket only when the storage answers that it is missing, so a
/// production key without the right to create buckets works against a pre-created bucket. New
/// buckets are private: S3 and MinIO grant no anonymous access unless a policy says so. It retries
/// connection failures and server errors while the storage is still starting (compose starts MinIO
/// alongside the command); configuration and authorisation errors fail at once.
/// </summary>
/// <param name="time">Wall-clock time: the storage starts in real time, whatever clock the host uses.</param>
internal sealed partial class StorageBootstrapper(
    IAmazonS3 client,
    string bucket,
    TimeProvider time,
    ILogger<StorageBootstrapper> logger,
    TimeSpan? retryDelay = null,
    TimeSpan? maxWait = null)
{
    public static readonly TimeSpan DefaultMaxWait = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromSeconds(1);

    public async Task EnsureBucketAsync(CancellationToken cancellationToken)
    {
        var deadline = time.GetUtcNow() + (maxWait ?? DefaultMaxWait);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await CreateIfMissingAsync(cancellationToken);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (IsTransient(exception) && time.GetUtcNow() < deadline)
            {
                var (failure, status) = (exception.GetType().Name, (exception as AmazonServiceException)?.StatusCode);
                LogRetrying(logger, attempt, failure, status);
                await Task.Delay(retryDelay ?? DefaultRetryDelay, time, cancellationToken);
            }
            catch (Exception exception) when (exception is AmazonServiceException or AmazonClientException or HttpRequestException or IOException or OperationCanceledException or TimeoutException)
            {
                throw new StorageUnavailableException("The object storage bucket could not be checked or created.", exception);
            }
        }
    }

    private async Task CreateIfMissingAsync(CancellationToken cancellationToken)
    {
        try
        {
            await client.HeadBucketAsync(new HeadBucketRequest { BucketName = bucket }, cancellationToken);
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            try
            {
                // UseClientRegion: providers outside us-east-1 need the location constraint.
                await client.PutBucketAsync(new PutBucketRequest { BucketName = bucket, UseClientRegion = true }, cancellationToken);
                LogCreated(logger);
            }
            catch (AmazonS3Exception created) when (created.ErrorCode is "BucketAlreadyOwnedByYou")
            {
                // Created meanwhile by another migrate run.
            }
        }
    }

    /// <summary>
    /// Worth waiting for: the storage is not listening yet, answers a server error, or times out. An
    /// SDK configuration or credential error, or a 4xx answer, will not fix itself.
    /// </summary>
    private static bool IsTransient(Exception exception) => exception switch
    {
        AmazonServiceException service => (int)service.StatusCode >= 500 || IsConnectionFailure(service.InnerException),
        AmazonClientException client => IsConnectionFailure(client.InnerException),
        _ => IsConnectionFailure(exception),
    };

    private static bool IsConnectionFailure(Exception? exception) =>
        exception is HttpRequestException or IOException or OperationCanceledException or TimeoutException
        || (exception?.InnerException is { } inner && IsConnectionFailure(inner));

    [LoggerMessage(Level = LogLevel.Information, Message = "Created the object storage bucket")]
    private static partial void LogCreated(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Object storage not reachable yet (attempt {Attempt}: {Failure} {Status}); retrying")]
    private static partial void LogRetrying(ILogger logger, int attempt, string failure, HttpStatusCode? status);
}
