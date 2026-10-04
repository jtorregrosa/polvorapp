using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PolvorApp.Api.Platform.Storage;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.SharedKernel.Storage;

namespace PolvorApp.Api.Tests.Registry;

/// <summary>
/// The registry's ID photo contract for the badges (add-badges, design D3): the stored JPEG, nothing
/// without an ID photo, a retry when the photo is replaced between its two reads, nothing and a logged
/// alert when the image is gone, and a storage outage surfaced to the caller.
/// </summary>
public sealed class IdPhotoReaderTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private readonly SwitchableStorage _storage = new();
    private RegistryTestHost _registry = null!;

    public async ValueTask InitializeAsync() => _registry = await RegistryTestHost.StartAsync(
        postgres, mailpit, services => services.AddSingleton<IObjectStorage>(provider => _storage.Wrap(provider.GetRequiredService<S3ObjectStorage>())));

    public async ValueTask DisposeAsync() => await _registry.DisposeAsync();

    [Fact]
    public async Task It_returns_the_stored_jpeg()
    {
        var id = await RegisterWithPhotoAsync();

        var photo = await ReadAsync(id);

        Assert.NotNull(photo);
        Assert.True(photo.Jpeg.Span[..2].SequenceEqual((byte[])[0xFF, 0xD8]));
        Assert.Equal("IdPhoto", photo.ToString());
    }

    [Fact]
    public async Task It_returns_nothing_without_an_id_photo_or_for_an_unknown_arquebusier()
    {
        var id = Id(await _registry.RegisterAsync(_registry.Own.Id));
        using (var license = await PhotoRequests.UploadAsync(_registry.Admin, id, "license-front", TestImages.Jpeg(1200, 800)))
        {
            Assert.True(license.IsSuccessStatusCode);
        }

        Assert.Null(await ReadAsync(id));
        Assert.Null(await ReadAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task A_photo_replaced_between_the_reads_is_read_again()
    {
        var id = await RegisterWithPhotoAsync();
        _storage.BeforeFirstGet = async () =>
        {
            using var replaced = await PhotoRequests.UploadAsync(_registry.Admin, id, "id", TestImages.Jpeg(900, 1200));
            Assert.True(replaced.IsSuccessStatusCode);
        };

        var photo = await ReadAsync(id);

        Assert.NotNull(photo);
        Assert.Equal(2, _storage.Gets);
    }

    [Fact]
    public async Task A_missing_image_gives_nothing_and_an_alert()
    {
        var id = await RegisterWithPhotoAsync();
        _storage.Missing = true;

        var photo = await ReadAsync(id);

        Assert.Null(photo);
        Assert.Contains(_registry.Host.Factory.Logs.Entries, e => e.Level >= LogLevel.Warning && e.Message.Contains(id.ToString(), StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_object_that_is_not_a_bounded_jpeg_gives_nothing_and_an_alert(bool oversized)
    {
        var id = await RegisterWithPhotoAsync();
        _storage.Replacement = oversized ? new byte[IdPhoto.MaxBytes + 1] : TestImages.Png(600, 800);

        var photo = await ReadAsync(id);

        Assert.Null(photo);
        Assert.Contains(_registry.Host.Factory.Logs.Entries, e => e.Level >= LogLevel.Warning && e.Message.Contains(id.ToString(), StringComparison.Ordinal));
    }

    [Fact]
    public void An_id_photo_is_a_bounded_jpeg()
    {
        Assert.Throws<ArgumentException>(() => new IdPhoto(ReadOnlyMemory<byte>.Empty));
        Assert.Throws<ArgumentException>(() => new IdPhoto(TestImages.Png(6, 8)));
        Assert.Throws<ArgumentException>(() => new IdPhoto(new byte[IdPhoto.MaxBytes + 1]));
        Assert.Equal(2, new IdPhoto(new byte[] { 0xFF, 0xD8 }).Jpeg.Length);
    }

    [Fact]
    public async Task A_storage_failure_while_the_image_streams_reaches_the_caller_as_an_outage()
    {
        var id = await RegisterWithPhotoAsync();
        _storage.FailMidStream = true;

        await Assert.ThrowsAsync<StorageUnavailableException>(() => ReadAsync(id));
    }

    [Fact]
    public async Task A_storage_outage_reaches_the_caller()
    {
        var id = await RegisterWithPhotoAsync();
        _storage.Outage = true;

        await Assert.ThrowsAsync<StorageUnavailableException>(() => ReadAsync(id));
    }

    private async Task<Guid> RegisterWithPhotoAsync()
    {
        var id = Id(await _registry.RegisterAsync(_registry.Own.Id));
        using var upload = await PhotoRequests.UploadAsync(_registry.Admin, id, "id", TestImages.Jpeg(600, 800));
        Assert.True(upload.IsSuccessStatusCode);
        return id;
    }

    private async Task<IdPhoto?> ReadAsync(Guid id)
    {
        await using var scope = _registry.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IIdPhotoReader>().ReadAsync(id, Token);
    }

    private static Guid Id(JsonElement arquebusier) => arquebusier.GetProperty("id").GetGuid();

    /// <summary>A response body that fails on the first read.</summary>
    private sealed class BrokenStream : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(new IOException("Synthetic connection reset."));
    }

    /// <summary>The real storage, with an outage, missing images or a hook before the first read switched on by a test.</summary>
    private sealed class SwitchableStorage
    {
        public bool Outage { get; set; }

        public bool Missing { get; set; }

        /// <summary>The image stream breaks after the response started, as a dropped connection does.</summary>
        public bool FailMidStream { get; set; }

        /// <summary>Bytes served instead of the stored image, with their real length.</summary>
        public byte[]? Replacement { get; set; }

        public Func<Task>? BeforeFirstGet { get; set; }

        public int Gets;

        public IObjectStorage Wrap(IObjectStorage inner) => new Switchable(inner, this);

        private sealed class Switchable(IObjectStorage inner, SwitchableStorage owner) : IObjectStorage
        {
            public Task PutAsync(string key, ReadOnlyMemory<byte> content, string contentType, CancellationToken cancellationToken) =>
                inner.PutAsync(key, content, contentType, cancellationToken);

            public async Task<StoredObject?> GetAsync(string key, CancellationToken cancellationToken)
            {
                if (owner.Outage)
                {
                    throw new StorageUnavailableException();
                }

                if (Interlocked.Increment(ref owner.Gets) == 1 && owner.BeforeFirstGet is { } hook)
                {
                    // The replacement commits and erases this image before it is read.
                    await hook();
                }

                if (owner.FailMidStream)
                {
                    return new StoredObject(new BrokenStream(), "image/jpeg", 50_000);
                }

                if (owner.Replacement is { } bytes)
                {
                    return new StoredObject(new MemoryStream(bytes), "image/jpeg", bytes.Length);
                }

                return owner.Missing ? null : await inner.GetAsync(key, cancellationToken);
            }

            public Task DeleteAsync(string key, CancellationToken cancellationToken) => inner.DeleteAsync(key, cancellationToken);

            public IAsyncEnumerable<StoredObjectInfo> ListAsync(string prefix, CancellationToken cancellationToken) =>
                inner.ListAsync(prefix, cancellationToken);
        }
    }
}
