using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.FederationCatalog.Comparsas;
using PolvorApp.FederationCatalog.Logos;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.SharedKernel.Images;
using PolvorApp.SharedKernel.Storage;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Catalog;

/// <summary>Specs "Comparsa logos", "Logo validation and processing" and "Logo access (BR-12)" (design D6).</summary>
[Collection(PostgresGroup.Name)]
public sealed class ComparsaLogoEndpointTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private RegistryTestHost _host = null!;

    public async ValueTask InitializeAsync() => _host = await RegistryTestHost.StartAsync(postgres, mailpit);

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task An_Admin_uploads_reads_and_removes_a_logo()
    {
        using var upload = await LogoRequests.UploadAsync(_host.Admin, _host.Other.Id, TestImages.Png(800, 400, transparent: true), "image/png");

        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        var logo = await ReadAsync<JsonElement>(upload);
        Assert.Equal((800, 400), (logo.GetProperty("width").GetInt32(), logo.GetProperty("height").GetInt32()));
        Assert.Equal(logo.GetProperty("version").GetGuid(), (await ComparsaAsync(_host.Admin, _host.Other.Id)).GetProperty("logo").GetProperty("version").GetGuid());

        using (var read = await _host.Admin.GetAsync(LogoRequests.Uri(_host.Other.Id), Token))
        {
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
            Assert.Equal("image/png", read.Content.Headers.ContentType?.MediaType);
            Assert.Equal("inline; filename=\"logo.png\"", read.Content.Headers.ContentDisposition?.ToString());
        }

        using var removal = await _host.Admin.DeleteAsync(LogoRequests.Uri(_host.Other.Id), Token);
        Assert.Equal(HttpStatusCode.NoContent, removal.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await ComparsaAsync(_host.Admin, _host.Other.Id)).GetProperty("logo").ValueKind);
        using var missing = await _host.Admin.GetAsync(LogoRequests.Uri(_host.Other.Id), Token);
        await AssertProblemAsync(missing, HttpStatusCode.NotFound, "logos.notFound");
        using var missingRemoval = await _host.Admin.DeleteAsync(LogoRequests.Uri(_host.Other.Id), Token);
        await AssertProblemAsync(missingRemoval, HttpStatusCode.NotFound, "logos.notFound");
    }

    [Fact]
    public async Task A_FiringChief_cannot_change_the_logo_of_their_own_comparsa()
    {
        var version = await UploadAsync(_host.Own.Id);

        using var upload = await LogoRequests.UploadAsync(_host.FiringChief, _host.Own.Id, TestImages.Png(900, 450));
        using var removal = await _host.FiringChief.DeleteAsync(LogoRequests.Uri(_host.Own.Id), Token);

        Assert.Equal(HttpStatusCode.Forbidden, upload.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, removal.StatusCode);
        // Nothing was replaced or removed: the Admin's logo is still there and still served.
        Assert.Equal(version, (await ComparsaAsync(_host.Admin, _host.Own.Id)).GetProperty("logo").GetProperty("version").GetGuid());
        using var read = await _host.Admin.GetAsync(LogoRequests.Uri(_host.Own.Id), Token);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
    }

    [Fact]
    public async Task An_inactive_comparsa_can_get_a_logo()
    {
        using (var deactivation = await _host.Admin.PostAsync($"/api/comparsas/{_host.Other.Id}/deactivate", new { }))
        {
            Assert.Equal(HttpStatusCode.OK, deactivation.StatusCode);
        }

        var version = await UploadAsync(_host.Other.Id);

        Assert.Equal(version, (await ComparsaAsync(_host.Admin, _host.Other.Id)).GetProperty("logo").GetProperty("version").GetGuid());
    }

    [Fact]
    public async Task A_FiringChief_reads_the_logo_of_their_comparsa_only()
    {
        await UploadAsync(_host.Own.Id);
        await UploadAsync(_host.Other.Id);

        using var own = await _host.FiringChief.GetAsync(LogoRequests.Uri(_host.Own.Id), Token);
        using var other = await _host.FiringChief.GetAsync(LogoRequests.Uri(_host.Other.Id), Token);

        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        Assert.Equal("image/png", own.Content.Headers.ContentType?.MediaType);
        Assert.True(own.Headers.CacheControl?.NoStore);
        Assert.Equal("nosniff", Assert.Single(own.Headers.GetValues("X-Content-Type-Options")));
        await AssertProblemAsync(other, HttpStatusCode.NotFound, "comparsas.notFound");
    }

    [Fact]
    public async Task Deactivating_and_reactivating_a_comparsa_keeps_its_logo()
    {
        var version = await UploadAsync(_host.Other.Id);

        using var deactivated = await _host.Admin.PostAsync($"/api/comparsas/{_host.Other.Id}/deactivate", new { });
        Assert.Equal(version, (await ComparsaAsync(_host.Admin, _host.Other.Id)).GetProperty("logo").GetProperty("version").GetGuid());
        using var reactivated = await _host.Admin.PostAsync($"/api/comparsas/{_host.Other.Id}/reactivate", new { });

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (deactivated.StatusCode, reactivated.StatusCode));
        Assert.Equal(version, (await ComparsaAsync(_host.Admin, _host.Other.Id)).GetProperty("logo").GetProperty("version").GetGuid());
    }

    [Fact]
    public async Task A_reference_without_its_image_reads_as_no_logo()
    {
        var logoId = Guid.CreateVersion7();
        var broken = new Comparsa
        {
            Id = Guid.CreateVersion7(),
            Name = "Comparsa Sintética Rota",
            Side = PolvorApp.FederationCatalog.Contracts.Side.Moorish,
            CreatedAt = DateTimeOffset.UtcNow,
            Logo = new ComparsaLogo
            {
                Id = logoId,
                ObjectKey = LogoStorage.KeyFor(logoId),
                Width = 512,
                Height = 256,
                SizeBytes = 2048,
                UploadedAt = DateTimeOffset.UtcNow,
            },
        };
        await _host.Services.SaveCatalogAsync(broken);

        using var read = await _host.Admin.GetAsync(LogoRequests.Uri(broken.Id), Token);
        await using var scope = _host.Services.CreateAsyncScope();
        var fromDirectory = await scope.ServiceProvider.GetRequiredService<PolvorApp.FederationCatalog.Contracts.ICatalogDirectory>()
            .ReadComparsaLogoAsync(broken.Id, Token);

        await AssertProblemAsync(read, HttpStatusCode.NotFound, "logos.notFound");
        Assert.Null(fromDirectory);
    }

    [Fact]
    public async Task Signed_out_requests_are_unauthorized()
    {
        using var anonymous = _host.Host.Factory.CreateClient();

        using var read = await anonymous.GetAsync(LogoRequests.Uri(_host.Own.Id), Token);

        Assert.Equal(HttpStatusCode.Unauthorized, read.StatusCode);
    }

    [Fact]
    public async Task An_unknown_comparsa_is_refused_before_the_body_is_read()
    {
        // Not even a readable form: had the body been read first, this would be a 400.
        using var garbage = new ByteArrayContent(Encoding.ASCII.GetBytes("--broken\r\nno headers"));
        garbage.Headers.TryAddWithoutValidation("Content-Type", "multipart/form-data; boundary=broken");

        using var response = await _host.Admin.PutAsync(LogoRequests.Uri(Guid.CreateVersion7()), garbage, Token);

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "comparsas.notFound");
    }

    [Theory]
    [InlineData("tooSmall")]
    [InlineData("aspectRatio")]
    [InlineData("unsupportedFormat")]
    [InlineData("tooLarge")]
    public async Task An_invalid_image_names_the_file_field(string reason)
    {
        var image = reason switch
        {
            "tooSmall" => TestImages.Png(200, 200),
            "aspectRatio" => TestImages.Png(1200, 300),
            "unsupportedFormat" => Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"512\" height=\"512\"/>"),
            _ => new byte[(int)LogoStorage.MaxUploadBytes + 1],
        };

        using var response = await LogoRequests.UploadAsync(_host.Admin, _host.Own.Id, image, "image/png");

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation");
        Assert.Equal(reason, (await ErrorsAsync(response))["file"]);
        Assert.Empty(await _host.Host.AuditEntriesAsync("ComparsaLogoUploaded"));
    }

    [Fact]
    public async Task A_form_without_a_file_is_required()
    {
        using var content = new MultipartFormDataContent { { new StringContent("not a file"), "other" } };

        using var response = await _host.Admin.PutAsync(LogoRequests.Uri(_host.Own.Id), content, Token);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation");
        Assert.Equal("required", (await ErrorsAsync(response))["file"]);
    }

    [Fact]
    public async Task An_upload_without_the_antiforgery_header_is_refused()
    {
        using var client = await _host.Host.SignInAsync(await _host.Host.CreateUserAsync("admin.sin.token@example.test", UserRole.Admin));
        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");

        using var response = await LogoRequests.UploadAsync(client, _host.Own.Id, TestImages.Png(800, 400));

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "antiforgery.invalid");
    }

    [Fact]
    public async Task The_list_and_the_detail_say_which_comparsas_have_a_logo()
    {
        var version = await UploadAsync(_host.Own.Id);

        using var list = await _host.Admin.GetAsync(new Uri("/api/comparsas?includeInactive=true", UriKind.Relative), Token);
        var rows = (await ReadAsync<JsonElement>(list)).EnumerateArray().ToDictionary(r => r.GetProperty("id").GetGuid(), r => r.GetProperty("logo"));

        Assert.Equal(version, rows[_host.Own.Id].GetProperty("version").GetGuid());
        Assert.Equal(JsonValueKind.Null, rows[_host.Other.Id].ValueKind);
        Assert.Equal(version, (await ComparsaAsync(_host.FiringChief, _host.Own.Id)).GetProperty("logo").GetProperty("version").GetGuid());
    }

    [Fact]
    public async Task The_upload_has_its_own_request_size_limit_and_rate_limit()
    {
        var endpoints = _host.Services.GetRequiredService<Microsoft.AspNetCore.Routing.EndpointDataSource>().Endpoints
            .OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.EndsWith("/logo", StringComparison.Ordinal) == true)
            .ToList();
        var upload = Assert.Single(endpoints, e => e.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()?.HttpMethods.Contains("PUT") == true);

        Assert.Equal(LogoStorage.MaxUploadBytes + (64 * 1024), upload.Metadata.GetMetadata<Microsoft.AspNetCore.Http.Metadata.IRequestSizeLimitMetadata>()?.MaxRequestBodySize);
        Assert.Equal(
            PolvorApp.SharedKernel.Security.RateLimitPolicies.ImageUploads,
            upload.Metadata.GetMetadata<Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute>()?.PolicyName);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private async Task<Guid> UploadAsync(Guid comparsaId)
    {
        using var response = await LogoRequests.UploadAsync(_host.Admin, comparsaId, TestImages.Png(800, 400));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await ReadAsync<JsonElement>(response)).GetProperty("version").GetGuid();
    }

    private static async Task<JsonElement> ComparsaAsync(HttpClient client, Guid id)
    {
        using var response = await client.GetAsync(new Uri($"/api/comparsas/{id}", UriKind.Relative), Token);
        return await ReadAsync<JsonElement>(response);
    }
}

/// <summary>Design D6: uploads are throttled per user by the <c>ImageUploads</c> policy.</summary>
[Collection(PostgresGroup.Name)]
public sealed class ComparsaLogoRateLimitTests(PostgresFixture postgres, MailpitFixture mailpit)
{
    [Fact]
    public async Task Logo_uploads_are_throttled_per_user()
    {
        await using var host = await RegistryTestHost.StartAsync(
            postgres, mailpit, settings: new Dictionary<string, string?> { ["RateLimits:ImageUploads:PermitLimit"] = "2" });
        using var second = await host.Host.SignInAsync(await host.Host.CreateUserAsync("admin.logos.dos@example.test", UserRole.Admin));

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
        {
            using var response = await LogoRequests.UploadAsync(host.Admin, host.Own.Id, TestImages.Png(800, 400));
            statuses.Add(response.StatusCode);
        }

        using var other = await LogoRequests.UploadAsync(second, host.Own.Id, TestImages.Png(800, 400));

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], statuses);
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
    }
}

/// <summary>Design D4: a busy image processor answers a retryable 503 and stores nothing.</summary>
[Collection(PostgresGroup.Name)]
public sealed class ComparsaLogoBusyTests(PostgresFixture postgres, MailpitFixture mailpit)
{
    [Fact]
    public async Task A_busy_processor_answers_service_unavailable()
    {
        await using var host = await RegistryTestHost.StartAsync(postgres, mailpit, services => services.AddSingleton<IImageNormalizer, BusyNormalizer>());

        using var response = await LogoRequests.UploadAsync(host.Admin, host.Own.Id, TestImages.Png(800, 400));

        await AssertProblemAsync(response, HttpStatusCode.ServiceUnavailable, "catalog.busy");
    }

    private sealed class BusyNormalizer : IImageNormalizer
    {
        public Task<ImageNormalization> NormalizeAsync(Stream input, ImageRules rules, CancellationToken cancellationToken) =>
            throw new ImageProcessingBusyException();
    }
}

/// <summary>Spec "Logo access (BR-12)": during a storage outage logo uploads and reads answer 503, the rest keeps working.</summary>
[Collection(PostgresGroup.Name)]
public sealed class ComparsaLogoOutageTests(PostgresFixture postgres, MailpitFixture mailpit)
{
    [Fact]
    public async Task A_storage_outage_affects_only_what_needs_the_storage()
    {
        await using var host = await RegistryTestHost.StartAsync(postgres, mailpit, services => services.AddSingleton<IObjectStorage, OutageStorage>());
        var logoId = Guid.CreateVersion7();
        var withLogo = new Comparsa
        {
            Id = Guid.CreateVersion7(),
            Name = "Comparsa Sintética Apagón",
            Side = PolvorApp.FederationCatalog.Contracts.Side.Moorish,
            CreatedAt = DateTimeOffset.UtcNow,
            Logo = new ComparsaLogo
            {
                Id = logoId,
                ObjectKey = LogoStorage.KeyFor(logoId),
                Width = 512,
                Height = 256,
                SizeBytes = 2048,
                UploadedAt = DateTimeOffset.UtcNow,
            },
        };
        await host.Services.SaveCatalogAsync(withLogo);

        using var upload = await LogoRequests.UploadAsync(host.Admin, host.Own.Id, TestImages.Png(800, 400));
        using var read = await host.Admin.GetAsync(LogoRequests.Uri(withLogo.Id), TestContext.Current.CancellationToken);
        using var edit = await host.Admin.PutAsJsonAsync(
            $"/api/comparsas/{withLogo.Id}", new { name = "Comparsa Sintética Apagada", side = "MOORISH" }, TestContext.Current.CancellationToken);
        using var removal = await host.Admin.DeleteAsync(LogoRequests.Uri(withLogo.Id), TestContext.Current.CancellationToken);

        await AssertProblemAsync(upload, HttpStatusCode.ServiceUnavailable, "storage.unavailable");
        await AssertProblemAsync(read, HttpStatusCode.ServiceUnavailable, "storage.unavailable");
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, removal.StatusCode);

        // The failed upload changed nothing.
        using var own = await host.Admin.GetAsync(new Uri($"/api/comparsas/{host.Own.Id}", UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.Equal(JsonValueKind.Null, (await ReadAsync<JsonElement>(own)).GetProperty("logo").ValueKind);
        Assert.Empty(await host.Host.AuditEntriesAsync("ComparsaLogoUploaded"));
    }

    [Fact]
    public async Task Another_module_reading_a_logo_during_an_outage_is_told_so()
    {
        await using var host = await RegistryTestHost.StartAsync(postgres, mailpit, services => services.AddSingleton<IObjectStorage, OutageStorage>());
        var logoId = Guid.CreateVersion7();
        var withLogo = new Comparsa
        {
            Id = Guid.CreateVersion7(),
            Name = "Comparsa Sintética Sin Luz",
            Side = PolvorApp.FederationCatalog.Contracts.Side.Christian,
            CreatedAt = DateTimeOffset.UtcNow,
            Logo = new ComparsaLogo
            {
                Id = logoId,
                ObjectKey = LogoStorage.KeyFor(logoId),
                Width = 512,
                Height = 256,
                SizeBytes = 2048,
                UploadedAt = DateTimeOffset.UtcNow,
            },
        };
        await host.Services.SaveCatalogAsync(withLogo);

        await using var scope = host.Services.CreateAsyncScope();
        var directory = scope.ServiceProvider.GetRequiredService<PolvorApp.FederationCatalog.Contracts.ICatalogDirectory>();

        await Assert.ThrowsAsync<StorageUnavailableException>(() => directory.ReadComparsaLogoAsync(withLogo.Id, TestContext.Current.CancellationToken));
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

/// <summary>Requests to the logo routes.</summary>
public static class LogoRequests
{
    public static Uri Uri(Guid comparsaId) => new($"/api/comparsas/{comparsaId}/logo", UriKind.Relative);

    public static async Task<HttpResponseMessage> UploadAsync(HttpClient client, Guid comparsaId, byte[] image, string contentType = "image/png")
    {
        using var file = new ByteArrayContent(image);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        using var content = new MultipartFormDataContent { { file, "file", "logo.png" } };
        return await client.PutAsync(Uri(comparsaId), content, TestContext.Current.CancellationToken);
    }
}
