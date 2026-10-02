using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Amazon.S3;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Platform.Images;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.SharedKernel.Images;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Registry;

/// <summary>Specs "Arquebusier photos (UC-01, UC-02)" and "Photo validation and processing" (uploads; design D5, D6).</summary>
public sealed class PhotoUploadTests(PostgresFixture postgres, MailpitFixture mailpit, MinioFixture minio) : IAsyncLifetime
{
    private readonly CountingNormalizer _normalizer = new();
    private RegistryTestHost _registry = null!;
    private JsonElement _own;
    private JsonElement _other;

    public async ValueTask InitializeAsync()
    {
        _registry = await RegistryTestHost.StartAsync(postgres, mailpit, services =>
            services.AddSingleton<IImageNormalizer>(provider => _normalizer.Wrapping(provider.GetRequiredService<SkiaImageNormalizer>())));
        _own = await _registry.RegisterAsync(_registry.Own.Id);
        _other = await _registry.RegisterAsync(_registry.Other.Id);
    }

    public async ValueTask DisposeAsync() => await _registry.DisposeAsync();

    [Fact]
    public async Task A_FiringChief_uploads_an_id_photo_without_changing_the_arquebusier_version()
    {
        using var response = await PhotoRequests.UploadAsync(_registry.FiringChief, Id(_own), "id", TestImages.Jpeg(600, 800));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var photo = await ReadAsync<JsonElement>(response);
        Assert.Equal("ID", photo.GetProperty("kind").GetString());
        Assert.Equal((600, 800), (photo.GetProperty("width").GetInt32(), photo.GetProperty("height").GetInt32()));
        var detail = await DetailAsync(Id(_own));
        Assert.Equal(photo.GetProperty("version").GetString(), detail.GetProperty("photos").GetProperty("id").GetProperty("version").GetString());
        Assert.Equal(_own.GetProperty("version").GetUInt32(), detail.GetProperty("version").GetUInt32());
        Assert.True(await ObjectExistsAsync(photo.GetProperty("version").GetGuid()));
    }

    [Fact]
    public async Task An_Admin_replaces_a_license_photo_and_the_previous_image_is_erased()
    {
        using var first = await PhotoRequests.UploadAsync(_registry.Admin, Id(_other), "license-front", TestImages.Jpeg(1000, 700));
        var firstVersion = (await ReadAsync<JsonElement>(first)).GetProperty("version").GetGuid();

        using var second = await PhotoRequests.UploadAsync(_registry.Admin, Id(_other), "license-front", TestImages.Png(1200, 800));

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var secondVersion = (await ReadAsync<JsonElement>(second)).GetProperty("version").GetGuid();
        Assert.NotEqual(firstVersion, secondVersion);
        Assert.False(await ObjectExistsAsync(firstVersion));
        Assert.True(await ObjectExistsAsync(secondVersion));
        var detail = await DetailAsync(Id(_other));
        Assert.Equal(secondVersion, detail.GetProperty("photos").GetProperty("licenseFront").GetProperty("version").GetGuid());
        Assert.Equal(_other.GetProperty("version").GetUInt32(), detail.GetProperty("version").GetUInt32());
    }

    [Fact]
    public async Task A_FiringChief_outside_the_scope_gets_not_found_before_any_work()
    {
        using var response = await PhotoRequests.UploadAsync(_registry.FiringChief, Id(_other), "id", TestImages.Jpeg(600, 800));

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "arquebusiers.notFound");
        Assert.Equal(0, _normalizer.Calls);
        Assert.Equal(JsonValueKind.Null, (await DetailAsync(Id(_other))).GetProperty("photos").GetProperty("id").ValueKind);
    }

    [Fact]
    public async Task An_unknown_arquebusier_is_not_found()
    {
        using var response = await PhotoRequests.UploadAsync(_registry.Admin, Guid.CreateVersion7(), "id", TestImages.Jpeg(600, 800));

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "arquebusiers.notFound");
    }

    [Fact]
    public async Task An_unknown_photo_kind_is_not_found()
    {
        using var response = await PhotoRequests.UploadAsync(_registry.Admin, Id(_own), "passport", TestImages.Jpeg(600, 800));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_license_photo_without_a_license_is_blocking()
    {
        var unlicensed = await _registry.RegisterAsync(_registry.Own.Id, body => body.Remove("license"));

        using var response = await PhotoRequests.UploadAsync(_registry.FiringChief, Id(unlicensed), "license-back", TestImages.Jpeg(1000, 700));

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "photos.noLicense");
        Assert.Equal(JsonValueKind.Null, (await DetailAsync(Id(unlicensed))).GetProperty("photos").GetProperty("licenseBack").ValueKind);
    }

    [Fact]
    public async Task A_pending_license_accepts_license_photos()
    {
        var pending = await _registry.RegisterAsync(_registry.Own.Id, body =>
            body["license"] = new Dictionary<string, object?> { ["type"] = "AE", ["pending"] = true });

        using var response = await PhotoRequests.UploadAsync(_registry.FiringChief, Id(pending), "license-front", TestImages.Jpeg(1000, 700));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_missing_file_is_required()
    {
        using var content = new MultipartFormDataContent { { new StringContent("not a file"), "other" } };

        using var response = await _registry.FiringChief.PutAsync(new Uri($"/api/arquebusiers/{Id(_own)}/photos/id", UriKind.Relative), content, Token);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation");
        Assert.Equal("required", (await ErrorsAsync(response))["file"]);
    }

    [Theory]
    [InlineData("pdf", "unsupportedFormat")]
    [InlineData("square", "aspectRatio")]
    [InlineData("small", "tooSmall")]
    [InlineData("pixels", "tooLarge")]
    public async Task Invalid_images_are_rejected_naming_the_file(string case_, string reason)
    {
        var image = case_ switch
        {
            "pdf" => TestImages.PdfHeader(),
            "square" => TestImages.Jpeg(1000, 1000),
            "small" => TestImages.Jpeg(300, 400),
            _ => TestImages.PngHeaderOnly(7000, 6000),
        };

        // A PDF named and declared as a JPEG: the content decides.
        using var response = await PhotoRequests.UploadAsync(_registry.FiringChief, Id(_own), "id", image, "photo.jpg", "image/jpeg");

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation");
        Assert.Equal(reason, (await ErrorsAsync(response))["file"]);
        Assert.Equal(JsonValueKind.Null, (await DetailAsync(Id(_own))).GetProperty("photos").GetProperty("id").ValueKind);
    }

    [Fact]
    public async Task A_file_over_ten_megabytes_is_too_large()
    {
        var image = new byte[(10 * 1024 * 1024) + 1];
        TestImages.Jpeg(600, 800).CopyTo(image, 0);

        using var response = await PhotoRequests.UploadAsync(_registry.FiringChief, Id(_own), "id", image);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation");
        Assert.Equal("tooLarge", (await ErrorsAsync(response))["file"]);
    }

    [Fact]
    public async Task An_upload_without_the_antiforgery_header_is_refused()
    {
        using var client = await _registry.SignInFiringChiefAgainAsync();
        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");

        using var response = await PhotoRequests.UploadAsync(client, Id(_own), "id", TestImages.Jpeg(600, 800));

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "antiforgery.invalid");
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static Guid Id(JsonElement arquebusier) => arquebusier.GetProperty("id").GetGuid();

    private async Task<JsonElement> DetailAsync(Guid id)
    {
        using var response = await _registry.Admin.GetAsync(new Uri($"/api/arquebusiers/{id}", UriKind.Relative), Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadAsync<JsonElement>(response);
    }

    private async Task<bool> ObjectExistsAsync(Guid photoId)
    {
        using var client = minio.CreateClient();
        try
        {
            await client.GetObjectMetadataAsync(MinioFixture.SharedBucket, $"registry/photos/{photoId:N}.jpg", Token);
            return true;
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    /// <summary>Counts normalisations, to prove that out-of-scope uploads cost no image work.</summary>
    private sealed class CountingNormalizer : IImageNormalizer
    {
        private IImageNormalizer? _inner;
        private int _calls;

        public int Calls => _calls;

        public CountingNormalizer Wrapping(IImageNormalizer inner)
        {
            _inner = inner;
            return this;
        }

        public Task<ImageNormalization> NormalizeAsync(Stream input, ImageRules rules, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return _inner!.NormalizeAsync(input, rules, cancellationToken);
        }
    }
}

/// <summary>Multipart photo uploads as the UI sends them.</summary>
public static class PhotoRequests
{
    public static async Task<HttpResponseMessage> UploadAsync(
        HttpClient client, Guid arquebusierId, string kind, byte[] image, string fileName = "photo.jpg", string contentType = "image/jpeg")
    {
        using var file = new ByteArrayContent(image);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        using var content = new MultipartFormDataContent { { file, "file", fileName } };
        return await client.PutAsync(new Uri($"/api/arquebusiers/{arquebusierId}/photos/{kind}", UriKind.Relative), content, TestContext.Current.CancellationToken);
    }
}
