using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Platform.Storage;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.SharedKernel.Images;
using PolvorApp.SharedKernel.Storage;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Registry;

/// <summary>Edge cases of the photo routes from the group 4 reviews (design D5, D6).</summary>
[Collection(PostgresGroup.Name)]
public sealed class PhotoEdgeTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private RegistryTestHost _registry = null!;
    private JsonElement _own;
    private JsonElement _other;

    public async ValueTask InitializeAsync()
    {
        _registry = await RegistryTestHost.StartAsync(postgres, mailpit);
        _own = await _registry.RegisterAsync(_registry.Own.Id);
        _other = await _registry.RegisterAsync(_registry.Other.Id);
    }

    public async ValueTask DisposeAsync() => await _registry.DisposeAsync();

    [Fact]
    public async Task An_out_of_scope_upload_is_refused_before_its_body_is_read()
    {
        // Not even a readable form: had the body been read first, this would be a 400.
        using var garbage = new ByteArrayContent(Encoding.ASCII.GetBytes("--broken\r\nno headers"));
        garbage.Headers.TryAddWithoutValidation("Content-Type", "multipart/form-data; boundary=broken");

        using var response = await _registry.FiringChief.PutAsync(PhotoUri(Id(_other), "id"), garbage, Token);

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "arquebusiers.notFound");
    }

    [Theory]
    [InlineData("ID")]
    [InlineData("License-Front")]
    public async Task Kind_slugs_are_lower_case_only(string kind)
    {
        using var read = await _registry.Admin.GetAsync(PhotoUri(Id(_own), kind), Token);
        using var upload = await PhotoRequests.UploadAsync(_registry.Admin, Id(_own), kind, TestImages.Jpeg(600, 800));

        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, upload.StatusCode);
    }

    [Fact]
    public async Task A_body_that_is_not_multipart_is_an_unsupported_media_type()
    {
        using var response = await _registry.Admin.PutAsync(
            PhotoUri(Id(_own), "id"), System.Net.Http.Json.JsonContent.Create(new { file = "not an upload" }), Token);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await DetailAsync(Id(_own))).GetProperty("photos").GetProperty("id").ValueKind);
    }

    [Fact]
    public async Task A_malformed_multipart_body_has_no_file()
    {
        using var broken = new ByteArrayContent(Encoding.ASCII.GetBytes("--broken\r\nno headers"));
        broken.Headers.TryAddWithoutValidation("Content-Type", "multipart/form-data; boundary=broken");

        using var response = await _registry.Admin.PutAsync(PhotoUri(Id(_own), "id"), broken, Token);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation");
        Assert.Equal("required", (await ErrorsAsync(response))["file"]);
    }

    [Fact]
    public async Task Signed_out_writes_are_unauthorized()
    {
        using var anonymous = _registry.Host.Factory.CreateClient();

        using var upload = await PhotoRequests.UploadAsync(anonymous, Id(_own), "id", TestImages.Jpeg(600, 800));
        using var remove = await anonymous.DeleteAsync(PhotoUri(Id(_own), "id"), Token);

        // Anti-forgery runs first for unsafe requests without a session: either way nothing happens.
        Assert.Contains(upload.StatusCode, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.BadRequest });
        Assert.Contains(remove.StatusCode, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.BadRequest });
        Assert.Equal(JsonValueKind.Null, (await DetailAsync(Id(_own))).GetProperty("photos").GetProperty("id").ValueKind);
    }

    [Fact]
    public async Task A_transfer_moves_access_to_the_photos_with_the_arquebusier()
    {
        using (var upload = await PhotoRequests.UploadAsync(_registry.Admin, Id(_own), "id", TestImages.Jpeg(600, 800)))
        {
            Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        }

        using (var transfer = await _registry.Admin.PostAsync($"/api/arquebusiers/{Id(_own)}/transfer", new { comparsaId = _registry.Other.Id }))
        {
            Assert.Equal(HttpStatusCode.NoContent, transfer.StatusCode);
        }

        using var chief = await _registry.FiringChief.GetAsync(PhotoUri(Id(_own), "id"), Token);
        using var admin = await _registry.Admin.GetAsync(PhotoUri(Id(_own), "id"), Token);
        await AssertProblemAsync(chief, HttpStatusCode.NotFound, "arquebusiers.notFound");
        Assert.Equal(HttpStatusCode.OK, admin.StatusCode);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static Guid Id(JsonElement arquebusier) => arquebusier.GetProperty("id").GetGuid();

    private static Uri PhotoUri(Guid id, string kind) => new($"/api/arquebusiers/{id}/photos/{kind}", UriKind.Relative);

    private async Task<JsonElement> DetailAsync(Guid id)
    {
        using var response = await _registry.Admin.GetAsync(new Uri($"/api/arquebusiers/{id}", UriKind.Relative), Token);
        return await ReadAsync<JsonElement>(response);
    }
}

/// <summary>
/// Spec "Stored photo cleanup": when erasing a replaced or removed image fails, the change still
/// succeeds and the image is left to the sweep; and a busy image processor answers a retryable 503.
/// </summary>
[Collection(PostgresGroup.Name)]
public sealed class PhotoCleanupFailureTests(PostgresFixture postgres, MailpitFixture mailpit, MinioFixture minio) : IAsyncLifetime
{
    private RegistryTestHost _registry = null!;
    private string _bucket = string.Empty;
    private Guid _arquebusier;

    public async ValueTask InitializeAsync()
    {
        _bucket = $"cleanup-{Guid.NewGuid():N}";
        _registry = await RegistryTestHost.StartAsync(
            postgres,
            mailpit,
            services => services.AddSingleton<IObjectStorage>(provider => new UndeletableStorage(provider.GetRequiredService<S3ObjectStorage>())),
            minio.SettingsFor(_bucket));
        _arquebusier = (await _registry.RegisterAsync(_registry.Own.Id)).GetProperty("id").GetGuid();
    }

    public async ValueTask DisposeAsync() => await _registry.DisposeAsync();

    [Fact]
    public async Task A_replacement_succeeds_when_the_previous_image_cannot_be_erased()
    {
        var first = await UploadAsync("license-front");

        var second = await UploadAsync("license-front");

        Assert.NotEqual(first, second);
        Assert.Equal(
            [$"registry/photos/{first:N}.jpg", $"registry/photos/{second:N}.jpg"],
            (await minio.ListKeysAsync(_bucket, "registry/photos/")).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Removing_a_photo_succeeds_when_its_image_cannot_be_erased()
    {
        var photo = await UploadAsync("id");
        var version = (await DetailAsync()).GetProperty("version").GetUInt32();

        using var response = await _registry.FiringChief.DeleteAsync(
            new Uri($"/api/arquebusiers/{_arquebusier}/photos/id", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var detail = await DetailAsync();
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("photos").GetProperty("id").ValueKind);
        // Photos are not personal data edits: the arquebusier's version is unchanged.
        Assert.Equal(version, detail.GetProperty("version").GetUInt32());
        // Left for the sweep.
        Assert.Equal([$"registry/photos/{photo:N}.jpg"], await minio.ListKeysAsync(_bucket, "registry/photos/"));
    }

    [Fact]
    public async Task Removing_the_license_succeeds_when_its_images_cannot_be_erased()
    {
        await UploadAsync("license-front");
        var detail = await DetailAsync();
        var body = RegistryTestHost.NewArquebusier(_registry.Own.Id);
        body.Remove("comparsaId");
        (body["nationalId"], body["federationId"], body["license"]) = (detail.GetProperty("nationalId").GetString(), detail.GetProperty("federationId").GetInt32(), null);
        body["status"] = "ACTIVE";
        body["version"] = detail.GetProperty("version").GetUInt32();

        using var response = await _registry.FiringChief.PutAsync(
            new Uri($"/api/arquebusiers/{_arquebusier}", UriKind.Relative), System.Net.Http.Json.JsonContent.Create(body), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await DetailAsync()).GetProperty("photos").GetProperty("licenseFront").ValueKind);
    }

    private async Task<Guid> UploadAsync(string kind)
    {
        using var response = await PhotoRequests.UploadAsync(_registry.Admin, _arquebusier, kind, kind == "id" ? TestImages.Jpeg(600, 800) : TestImages.Jpeg(1000, 700));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await ReadAsync<JsonElement>(response)).GetProperty("version").GetGuid();
    }

    private async Task<JsonElement> DetailAsync()
    {
        using var response = await _registry.Admin.GetAsync(new Uri($"/api/arquebusiers/{_arquebusier}", UriKind.Relative), TestContext.Current.CancellationToken);
        return await ReadAsync<JsonElement>(response);
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

/// <summary>Design D3: a busy image processor answers a retryable 503 and stores nothing.</summary>
[Collection(PostgresGroup.Name)]
public sealed class PhotoBusyTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private RegistryTestHost _registry = null!;

    public async ValueTask InitializeAsync() =>
        _registry = await RegistryTestHost.StartAsync(postgres, mailpit, services => services.AddSingleton<IImageNormalizer, BusyNormalizer>());

    public async ValueTask DisposeAsync() => await _registry.DisposeAsync();

    [Fact]
    public async Task A_busy_processor_answers_service_unavailable()
    {
        var arquebusier = (await _registry.RegisterAsync(_registry.Own.Id)).GetProperty("id").GetGuid();

        using var response = await PhotoRequests.UploadAsync(_registry.Admin, arquebusier, "id", TestImages.Jpeg(600, 800));

        await AssertProblemAsync(response, HttpStatusCode.ServiceUnavailable, "registry.busy");
    }

    private sealed class BusyNormalizer : IImageNormalizer
    {
        public Task<ImageNormalization> NormalizeAsync(Stream input, ImageRules rules, CancellationToken cancellationToken) =>
            throw new ImageProcessingBusyException();
    }
}
