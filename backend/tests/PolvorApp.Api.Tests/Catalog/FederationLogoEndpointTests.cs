using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.FederationCatalog.Logos;
using PolvorApp.FederationCatalog.Persistence;
using PolvorApp.SharedKernel.Storage;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Catalog;

/// <summary>
/// Spec "Federation logo" (add-distribution-planning, design D11): uploaded at run time by Admins,
/// processed like comparsa logos, read by every signed-in user, never cached, audited without the image.
/// Synthetic generated images only.
/// </summary>
public sealed class FederationLogoEndpointTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private RegistryTestHost _host = null!;

    public async ValueTask InitializeAsync() => _host = await RegistryTestHost.StartAsync(postgres, mailpit);

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task There_is_no_logo_until_an_Admin_uploads_one()
    {
        Assert.Equal(JsonValueKind.Null, (await SettingsAsync(_host.Admin)).GetProperty("logo").ValueKind);

        using var read = await _host.Admin.GetAsync(FederationLogoRequests.Uri, Token);

        await AssertProblemAsync(read, HttpStatusCode.NotFound, "logos.notFound");
    }

    [Fact]
    public async Task An_Admin_uploads_replaces_and_removes_the_logo()
    {
        using var upload = await FederationLogoRequests.UploadAsync(_host.Admin, TestImages.Png(800, 400, transparent: true));
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        var first = await ReadAsync<JsonElement>(upload);
        Assert.Equal((800, 400), (first.GetProperty("width").GetInt32(), first.GetProperty("height").GetInt32()));
        Assert.Equal(first.GetProperty("version").GetGuid(), (await SettingsAsync(_host.Admin)).GetProperty("logo").GetProperty("version").GetGuid());

        using (var read = await _host.Admin.GetAsync(FederationLogoRequests.Uri, Token))
        {
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
            Assert.Equal("image/png", read.Content.Headers.ContentType?.MediaType);
            Assert.True(read.Headers.CacheControl?.NoStore);
        }

        using var replacement = await FederationLogoRequests.UploadAsync(_host.Admin, TestImages.Jpeg(600, 600));
        var second = await ReadAsync<JsonElement>(replacement);
        Assert.NotEqual(first.GetProperty("version").GetGuid(), second.GetProperty("version").GetGuid());

        using var removal = await _host.Admin.DeleteAsync(FederationLogoRequests.Uri, Token);
        Assert.Equal(HttpStatusCode.NoContent, removal.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await SettingsAsync(_host.Admin)).GetProperty("logo").ValueKind);
        using var missingRemoval = await _host.Admin.DeleteAsync(FederationLogoRequests.Uri, Token);
        await AssertProblemAsync(missingRemoval, HttpStatusCode.NotFound, "logos.notFound");
    }

    [Fact]
    public async Task A_FiringChief_reads_the_logo_but_cannot_change_it()
    {
        await UploadAsync();

        using var read = await _host.FiringChief.GetAsync(FederationLogoRequests.Uri, Token);
        using var upload = await FederationLogoRequests.UploadAsync(_host.FiringChief, TestImages.Png(800, 400));
        using var removal = await _host.FiringChief.DeleteAsync(FederationLogoRequests.Uri, Token);

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(JsonValueKind.Object, (await SettingsAsync(_host.FiringChief)).GetProperty("logo").ValueKind);
        Assert.Equal((HttpStatusCode.Forbidden, HttpStatusCode.Forbidden), (upload.StatusCode, removal.StatusCode));
    }

    [Fact]
    public async Task Signed_out_requests_are_unauthorized()
    {
        using var anonymous = _host.Host.Factory.CreateClient();

        using var read = await anonymous.GetAsync(FederationLogoRequests.Uri, Token);
        using var settings = await anonymous.GetAsync(FederationLogoRequests.SettingsUri, Token);

        Assert.Equal((HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized), (read.StatusCode, settings.StatusCode));
    }

    [Theory]
    [InlineData("tooSmall")]
    [InlineData("aspectRatio")]
    [InlineData("unsupportedFormat")]
    [InlineData("tooLarge")]
    public async Task An_invalid_image_names_the_file_field_and_stores_nothing(string reason)
    {
        var image = reason switch
        {
            "tooSmall" => TestImages.Png(200, 200),
            "aspectRatio" => TestImages.Png(1200, 300),
            "unsupportedFormat" => System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"512\" height=\"512\"/>"),
            _ => new byte[(int)LogoStorage.MaxUploadBytes + 1],
        };

        using var response = await FederationLogoRequests.UploadAsync(_host.Admin, image);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation");
        Assert.Equal(reason, (await ErrorsAsync(response))["file"]);
        Assert.Equal(JsonValueKind.Null, (await SettingsAsync(_host.Admin)).GetProperty("logo").ValueKind);
        Assert.Empty(await _host.Host.AuditEntriesAsync("FederationLogoUploaded"));
    }

    [Fact]
    public async Task A_form_without_a_file_is_required()
    {
        using var content = new MultipartFormDataContent { { new StringContent("not a file"), "other" } };

        using var response = await _host.Admin.PutAsync(FederationLogoRequests.Uri, content, Token);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation");
        Assert.Equal("required", (await ErrorsAsync(response))["file"]);
    }

    [Fact]
    public async Task An_upload_or_removal_without_the_antiforgery_header_is_refused()
    {
        using var client = await _host.Host.SignInAsync(await _host.Host.CreateUserAsync("admin.federacion.sin.token@example.test", PolvorApp.IdentityAccess.Contracts.UserRole.Admin));
        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");

        using var upload = await FederationLogoRequests.UploadAsync(client, TestImages.Png(800, 400));
        using var removal = await client.DeleteAsync(FederationLogoRequests.Uri, Token);

        await AssertProblemAsync(upload, HttpStatusCode.BadRequest, "antiforgery.invalid");
        await AssertProblemAsync(removal, HttpStatusCode.BadRequest, "antiforgery.invalid");
    }

    [Fact]
    public async Task Uploads_and_removals_are_audited_without_the_image()
    {
        var version = await UploadAsync();
        await UploadAsync();
        using var removal = await _host.Admin.DeleteAsync(FederationLogoRequests.Uri, Token);

        var uploads = await _host.Host.AuditEntriesAsync("FederationLogoUploaded");
        var removed = Assert.Single(await _host.Host.AuditEntriesAsync("FederationLogoRemoved"));

        Assert.Equal(2, uploads.Count);
        Assert.All(uploads.Append(removed), entry => Assert.Equal((_host.AdminId, "FederationSettings", "1", (Guid?)null), (entry.ActorUserId, entry.EntityType, entry.EntityId, entry.ComparsaId)));
        Assert.Equal([false, true], uploads.Select(e => JsonDocument.Parse(e.Data!).RootElement.GetProperty("replaced").GetBoolean()).Order());
        Assert.All(uploads, entry => Assert.Equal(["replaced"], JsonDocument.Parse(entry.Data!).RootElement.EnumerateObject().Select(p => p.Name)));
        Assert.All(uploads, entry => Assert.DoesNotContain(version.ToString("N"), entry.Data!, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task The_upload_has_the_logo_size_limit_and_rate_limit()
    {
        var upload = Assert.Single(
            _host.Services.GetRequiredService<Microsoft.AspNetCore.Routing.EndpointDataSource>().Endpoints
                .OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>()
                .Where(e => e.RoutePattern.RawText == "/api/federation-logo"),
            e => e.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()?.HttpMethods.Contains("PUT") == true);

        Assert.Equal(PolvorApp.FederationCatalog.Endpoints.LogoEndpoints.MaxRequestBytes, upload.Metadata.GetMetadata<Microsoft.AspNetCore.Http.Metadata.IRequestSizeLimitMetadata>()?.MaxRequestBodySize);
        Assert.Equal(
            PolvorApp.SharedKernel.Security.RateLimitPolicies.ImageUploads,
            upload.Metadata.GetMetadata<Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute>()?.PolicyName);
    }

    private async Task<Guid> UploadAsync()
    {
        using var response = await FederationLogoRequests.UploadAsync(_host.Admin, TestImages.Png(800, 400));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await ReadAsync<JsonElement>(response)).GetProperty("version").GetGuid();
    }

    private static async Task<JsonElement> SettingsAsync(HttpClient client)
    {
        using var response = await client.GetAsync(FederationLogoRequests.SettingsUri, Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadAsync<JsonElement>(response);
    }
}

/// <summary>
/// Spec "Federation logo" with the storage (design D11): a replacement erases the previous image, a
/// removal erases it, and the orphan sweep keeps the referenced one. Each test has its own bucket.
/// </summary>
public sealed class FederationLogoStorageTests(PostgresFixture postgres, MailpitFixture mailpit, MinioFixture minio) : IAsyncLifetime
{
    private RegistryTestHost _host = null!;
    private string _bucket = string.Empty;

    public async ValueTask InitializeAsync()
    {
        _bucket = await minio.CreateBucketAsync();
        _host = await RegistryTestHost.StartAsync(postgres, mailpit, settings: minio.SettingsFor(_bucket));
    }

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task A_replacement_erases_the_previous_image_and_a_removal_erases_the_last()
    {
        await UploadAsync();
        var second = await UploadAsync();

        Assert.Equal([LogoStorage.KeyFor(second)], await minio.ListKeysAsync(_bucket, LogoStorage.Prefix));

        using var removal = await _host.Admin.DeleteAsync(FederationLogoRequests.Uri, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, removal.StatusCode);
        Assert.Empty(await minio.ListKeysAsync(_bucket, LogoStorage.Prefix));
    }

    [Fact]
    public async Task The_sweep_owner_reports_the_federation_logo_as_referenced()
    {
        var key = LogoStorage.KeyFor(await UploadAsync());

        await using var scope = _host.Services.CreateAsyncScope();
        var owner = Assert.Single(scope.ServiceProvider.GetServices<IStoredObjectOwner>(), o => o.Prefix == LogoStorage.Prefix);

        Assert.Equal([key], await owner.FilterReferencedAsync([key, LogoStorage.KeyFor(Guid.CreateVersion7())], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Another_module_reads_no_logo_before_the_upload()
    {
        await using var scope = _host.Services.CreateAsyncScope();

        Assert.Null(await scope.ServiceProvider.GetRequiredService<ICatalogDirectory>()
            .ReadFederationLogoAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Another_module_reads_the_logo_image()
    {
        await UploadAsync();

        await using var scope = _host.Services.CreateAsyncScope();
        var logo = await scope.ServiceProvider.GetRequiredService<ICatalogDirectory>()
            .ReadFederationLogoAsync(TestContext.Current.CancellationToken);

        Assert.Equal((800, 400), (logo!.Width, logo.Height));
        Assert.True(logo.Png.Span[..4].SequenceEqual((byte[])[0x89, 0x50, 0x4E, 0x47]));
    }

    private async Task<Guid> UploadAsync()
    {
        using var response = await FederationLogoRequests.UploadAsync(_host.Admin, TestImages.Png(800, 400));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await ReadAsync<JsonElement>(response)).GetProperty("version").GetGuid();
    }
}

/// <summary>Spec "Federation logo": during a storage outage the logo answers 503 and other modules are told so.</summary>
public sealed class FederationLogoOutageTests(PostgresFixture postgres, MailpitFixture mailpit)
{
    [Fact]
    public async Task A_storage_outage_answers_service_unavailable()
    {
        await using var host = await RegistryTestHost.StartAsync(postgres, mailpit, services => services.AddSingleton<IObjectStorage, OutageStorage>());

        using var upload = await FederationLogoRequests.UploadAsync(host.Admin, TestImages.Png(800, 400));

        await AssertProblemAsync(upload, HttpStatusCode.ServiceUnavailable, "storage.unavailable");
        Assert.Empty(await host.Host.AuditEntriesAsync("FederationLogoUploaded"));
    }

    [Fact]
    public async Task Another_module_reading_the_logo_during_an_outage_is_told_so()
    {
        await using var host = await RegistryTestHost.StartAsync(postgres, mailpit, services => services.AddSingleton<IObjectStorage, OutageStorage>());
        var logoId = Guid.CreateVersion7();
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FederationCatalogDbContext>();
            var settings = await db.FederationSettings.SingleAsync(TestContext.Current.CancellationToken);
            settings.Logo = new ComparsaLogo
            {
                Id = logoId,
                ObjectKey = LogoStorage.KeyFor(logoId),
                Width = 512,
                Height = 256,
                SizeBytes = 2048,
                UploadedAt = DateTimeOffset.UtcNow,
            };
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var read = host.Services.CreateAsyncScope();
        var directory = read.ServiceProvider.GetRequiredService<ICatalogDirectory>();

        await Assert.ThrowsAsync<StorageUnavailableException>(() => directory.ReadFederationLogoAsync(TestContext.Current.CancellationToken));
    }

    private sealed class OutageStorage : IObjectStorage
    {
        public Task PutAsync(string key, ReadOnlyMemory<byte> content, string contentType, CancellationToken cancellationToken) =>
            throw new StorageUnavailableException();

        public Task<StoredObject?> GetAsync(string key, CancellationToken cancellationToken) => throw new StorageUnavailableException();

        public Task DeleteAsync(string key, CancellationToken cancellationToken) => throw new StorageUnavailableException();

        public async IAsyncEnumerable<StoredObjectInfo> ListAsync(string prefix, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            throw new StorageUnavailableException();
#pragma warning disable CS0162 // An iterator needs a yield.
            yield break;
#pragma warning restore CS0162
        }
    }
}

/// <summary>Requests to the Federation logo routes.</summary>
public static class FederationLogoRequests
{
    public static readonly Uri Uri = new("/api/federation-logo", UriKind.Relative);

    public static readonly Uri SettingsUri = new("/api/federation", UriKind.Relative);

    public static async Task<HttpResponseMessage> UploadAsync(HttpClient client, byte[] image, string contentType = "image/png")
    {
        using var file = new ByteArrayContent(image);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        using var content = new MultipartFormDataContent { { file, "file", "logo.png" } };
        return await client.PutAsync(Uri, content, TestContext.Current.CancellationToken);
    }
}
