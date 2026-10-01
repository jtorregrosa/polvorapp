using System.Net;
using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using PolvorApp.Api.Tests.Infrastructure;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Registry;

/// <summary>
/// Specs "Private photo access (SEC-02)", "Arquebusier photos" (removal), "Current license" (license
/// photos) and "Deleting an arquebusier" (photos); design D5, D7.
/// </summary>
[Collection(PostgresGroup.Name)]
public sealed class PhotoAccessTests(PostgresFixture postgres, MailpitFixture mailpit, MinioFixture minio) : IAsyncLifetime
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
    public async Task A_photo_is_streamed_as_an_uncached_jpeg_with_a_fixed_name()
    {
        await UploadAsync(Id(_own), "id", TestImages.Jpeg(600, 800), "Apellido Nombre 12345678Z.jpg");

        using var response = await _registry.FiringChief.GetAsync(PhotoUri(Id(_own), "id"), Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/jpeg", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Contains("nosniff", response.Headers.GetValues("X-Content-Type-Options"));
        Assert.Equal("inline; filename=\"photo.jpg\"", string.Join(",", response.Content.Headers.GetValues("Content-Disposition")));
        var bytes = await response.Content.ReadAsByteArrayAsync(Token);
        Assert.Equal(bytes.Length, response.Content.Headers.ContentLength);
        Assert.Equal([0xFF, 0xD8], bytes[..2]);
    }

    [Fact]
    public async Task A_missing_photo_is_not_found()
    {
        using var response = await _registry.FiringChief.GetAsync(PhotoUri(Id(_own), "license-front"), Token);

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "photos.notFound");
    }

    [Fact]
    public async Task An_unknown_kind_is_not_found()
    {
        using var response = await _registry.FiringChief.GetAsync(PhotoUri(Id(_own), "passport"), Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_FiringChief_cannot_read_or_remove_another_comparsas_photo()
    {
        await UploadAsync(Id(_other), "id", TestImages.Jpeg(600, 800));

        using var read = await _registry.FiringChief.GetAsync(PhotoUri(Id(_other), "id"), Token);
        using var remove = await _registry.FiringChief.DeleteAsync(PhotoUri(Id(_other), "id"), Token);

        await AssertProblemAsync(read, HttpStatusCode.NotFound, "arquebusiers.notFound");
        await AssertProblemAsync(remove, HttpStatusCode.NotFound, "arquebusiers.notFound");
        Assert.NotEqual(JsonValueKind.Null, (await DetailAsync(Id(_other))).GetProperty("photos").GetProperty("id").ValueKind);
    }

    [Fact]
    public async Task A_signed_out_request_gets_no_image()
    {
        await UploadAsync(Id(_own), "id", TestImages.Jpeg(600, 800));
        using var anonymous = _registry.Host.Factory.CreateClient();

        using var response = await anonymous.GetAsync(PhotoUri(Id(_own), "id"), Token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual("image/jpeg", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task A_reference_without_its_image_is_not_found_and_logged_as_an_error()
    {
        var version = await UploadAsync(Id(_own), "id", TestImages.Jpeg(600, 800));
        using (var client = minio.CreateClient())
        {
            await client.DeleteObjectAsync(MinioFixture.SharedBucket, Key(version), Token);
        }

        using var response = await _registry.FiringChief.GetAsync(PhotoUri(Id(_own), "id"), Token);

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "photos.notFound");
        Assert.Contains(_registry.Host.Factory.Logs.Entries, e => e.Level == LogLevel.Error && e.Message.Contains(version.ToString(), StringComparison.Ordinal));
    }

    [Fact]
    public async Task Removing_a_photo_removes_the_reference_and_erases_the_image()
    {
        var version = await UploadAsync(Id(_own), "id", TestImages.Jpeg(600, 800));

        using var response = await _registry.FiringChief.DeleteAsync(PhotoUri(Id(_own), "id"), Token);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await DetailAsync(Id(_own))).GetProperty("photos").GetProperty("id").ValueKind);
        Assert.False(await ObjectExistsAsync(version));
        using var again = await _registry.FiringChief.DeleteAsync(PhotoUri(Id(_own), "id"), Token);
        await AssertProblemAsync(again, HttpStatusCode.NotFound, "photos.notFound");
    }

    [Fact]
    public async Task List_rows_say_whether_there_is_an_id_photo()
    {
        await UploadAsync(Id(_own), "id", TestImages.Jpeg(600, 800));
        var without = await _registry.RegisterAsync(_registry.Own.Id);

        using var response = await _registry.Admin.GetAsync(new Uri($"/api/arquebusiers?comparsaId={_registry.Own.Id}", UriKind.Relative), Token);

        var rows = (await ReadAsync<JsonElement>(response)).EnumerateArray().ToDictionary(r => r.GetProperty("id").GetGuid(), r => r.GetProperty("hasIdPhoto").GetBoolean());
        Assert.True(rows[Id(_own)]);
        Assert.False(rows[Id(without)]);
    }

    [Fact]
    public async Task Removing_the_license_erases_its_photos_and_keeps_the_id_photo()
    {
        var id = await UploadAsync(Id(_own), "id", TestImages.Jpeg(600, 800));
        var front = await UploadAsync(Id(_own), "license-front", TestImages.Jpeg(1000, 700));
        var back = await UploadAsync(Id(_own), "license-back", TestImages.Jpeg(1000, 700));

        await EditAsync(Id(_own), body => body["license"] = null);

        var photos = (await DetailAsync(Id(_own))).GetProperty("photos");
        Assert.Equal(id, photos.GetProperty("id").GetProperty("version").GetGuid());
        Assert.Equal(JsonValueKind.Null, photos.GetProperty("licenseFront").ValueKind);
        Assert.Equal(JsonValueKind.Null, photos.GetProperty("licenseBack").ValueKind);
        Assert.False(await ObjectExistsAsync(front));
        Assert.False(await ObjectExistsAsync(back));
        Assert.True(await ObjectExistsAsync(id));
        var audit = Assert.Single(await _registry.Host.AuditEntriesAsync("ArquebusierUpdated"));
        using var data = JsonDocument.Parse(audit.Data!);
        Assert.Equal(["LICENSE_BACK", "LICENSE_FRONT"], data.RootElement.GetProperty("removedPhotos").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task A_renewal_keeps_the_license_photos()
    {
        var front = await UploadAsync(Id(_own), "license-front", TestImages.Jpeg(1000, 700));
        var renewed = RegistryTestHost.DefaultIssuedOn.AddMonths(6);

        await EditAsync(Id(_own), body => body["license"] = new Dictionary<string, object?>
        {
            ["type"] = "AE",
            ["pending"] = false,
            ["issuedOn"] = RegistryTestHost.Iso(renewed),
        });

        Assert.Equal(front, (await DetailAsync(Id(_own))).GetProperty("photos").GetProperty("licenseFront").GetProperty("version").GetGuid());
        Assert.True(await ObjectExistsAsync(front));
    }

    [Fact]
    public async Task Deleting_an_arquebusier_erases_their_photos()
    {
        var versions = new[]
        {
            await UploadAsync(Id(_own), "id", TestImages.Jpeg(600, 800)),
            await UploadAsync(Id(_own), "license-front", TestImages.Jpeg(1000, 700)),
            await UploadAsync(Id(_own), "license-back", TestImages.Jpeg(1000, 700)),
        };

        using var response = await _registry.FiringChief.DeleteAsync(new Uri($"/api/arquebusiers/{Id(_own)}", UriKind.Relative), Token);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        foreach (var version in versions)
        {
            Assert.False(await ObjectExistsAsync(version));
        }

        using var photo = await _registry.Admin.GetAsync(PhotoUri(Id(_own), "id"), Token);
        await AssertProblemAsync(photo, HttpStatusCode.NotFound, "arquebusiers.notFound");
        var audit = Assert.Single(await _registry.Host.AuditEntriesAsync("ArquebusierDeleted"));
        using var data = JsonDocument.Parse(audit.Data!);
        Assert.Equal(3, data.RootElement.GetProperty("photoCount").GetInt32());
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static Guid Id(JsonElement arquebusier) => arquebusier.GetProperty("id").GetGuid();

    private static Uri PhotoUri(Guid id, string kind) => new($"/api/arquebusiers/{id}/photos/{kind}", UriKind.Relative);

    private static string Key(Guid version) => $"registry/photos/{version:N}.jpg";

    private async Task<Guid> UploadAsync(Guid id, string kind, byte[] image, string fileName = "photo.jpg")
    {
        using var response = await PhotoRequests.UploadAsync(_registry.Admin, id, kind, image, fileName);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await ReadAsync<JsonElement>(response)).GetProperty("version").GetGuid();
    }

    /// <summary>Saves the arquebusier's current detail with a change, as the edit form does.</summary>
    private async Task EditAsync(Guid id, Action<Dictionary<string, object?>> change)
    {
        var detail = await DetailAsync(id);
        var body = new Dictionary<string, object?>
        {
            ["federationId"] = detail.GetProperty("federationId").GetInt32(),
            ["nationalId"] = detail.GetProperty("nationalId").GetString(),
            ["firstName"] = detail.GetProperty("firstName").GetString(),
            ["lastName"] = detail.GetProperty("lastName").GetString(),
            ["birthDate"] = detail.GetProperty("birthDate").GetString(),
            ["gender"] = detail.GetProperty("gender").GetString(),
            ["status"] = detail.GetProperty("status").GetString(),
            ["license"] = new Dictionary<string, object?> { ["type"] = "AE", ["pending"] = false, ["issuedOn"] = RegistryTestHost.Iso(RegistryTestHost.DefaultIssuedOn) },
            ["version"] = detail.GetProperty("version").GetUInt32(),
        };
        change(body);
        using var response = await _registry.FiringChief.PutAsync(
            new Uri($"/api/arquebusiers/{id}", UriKind.Relative), System.Net.Http.Json.JsonContent.Create(body), Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<JsonElement> DetailAsync(Guid id)
    {
        using var response = await _registry.Admin.GetAsync(new Uri($"/api/arquebusiers/{id}", UriKind.Relative), Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadAsync<JsonElement>(response);
    }

    private async Task<bool> ObjectExistsAsync(Guid version)
    {
        using var client = minio.CreateClient();
        try
        {
            await client.GetObjectMetadataAsync(new GetObjectMetadataRequest { BucketName = MinioFixture.SharedBucket, Key = Key(version) }, Token);
            return true;
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }
}
