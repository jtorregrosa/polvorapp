namespace PolvorApp.SharedKernel.Storage;

/// <summary>
/// Private S3-compatible object storage (ADR-0005; spec: Private object storage). Each module keeps
/// its objects under its own key prefix (<c>&lt;module&gt;/&lt;collection&gt;/&lt;uuid&gt;.&lt;ext&gt;</c>), with
/// random names that never derive from personal data.
/// </summary>
public interface IObjectStorage
{
    /// <summary>Stores or overwrites an object.</summary>
    /// <exception cref="StorageUnavailableException">The storage could not be reached or refused the request.</exception>
    Task PutAsync(string key, ReadOnlyMemory<byte> content, string contentType, CancellationToken cancellationToken);

    /// <summary>Opens an object for reading; the caller disposes the result.</summary>
    /// <returns>Null when the object does not exist.</returns>
    /// <exception cref="StorageUnavailableException">The storage could not be reached or refused the request.</exception>
    Task<StoredObject?> GetAsync(string key, CancellationToken cancellationToken);

    /// <summary>Deletes an object; deleting a missing object succeeds.</summary>
    /// <exception cref="StorageUnavailableException">The storage could not be reached or refused the request.</exception>
    Task DeleteAsync(string key, CancellationToken cancellationToken);

    /// <summary>Every object whose key starts with <paramref name="prefix"/>, in key order.</summary>
    /// <exception cref="StorageUnavailableException">The storage could not be reached or refused the request.</exception>
    IAsyncEnumerable<StoredObjectInfo> ListAsync(string prefix, CancellationToken cancellationToken);
}

/// <summary>An object opened for reading.</summary>
public sealed class StoredObject(Stream content, string contentType, long length) : IAsyncDisposable, IDisposable
{
    public Stream Content { get; } = content;

    public string ContentType { get; } = contentType;

    public long Length { get; } = length;

    public ValueTask DisposeAsync() => Content.DisposeAsync();

    public void Dispose() => Content.Dispose();
}

/// <summary>A listed object: its key and when it was last written.</summary>
public sealed record StoredObjectInfo(string Key, DateTimeOffset LastModified);

/// <summary>
/// The storage could not be reached or refused the request. Its message is generic; the storage
/// library's exception is kept as the inner exception for the server log only, never for a response.
/// Object keys are random and credentials never appear in library messages (NFR-12).
/// </summary>
public sealed class StorageUnavailableException : Exception
{
    public StorageUnavailableException()
        : base("The object storage is unavailable.")
    {
    }

    public StorageUnavailableException(string message)
        : base(message)
    {
    }

    public StorageUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
