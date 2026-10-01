using System.Net;
using System.Text.Json;
using PolvorApp.Api.Tests.Infrastructure;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Registry;

/// <summary>Spec "Registry changes are audited" (photos), design D8: kinds only, never images, keys or file names.</summary>
[Collection(PostgresGroup.Name)]
public sealed class PhotoAuditTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    /// <summary>A client file name that would be personal data (synthetic).</summary>
    private const string PersonalFileName = "Sintetica Apellido 12345678Z.jpg";

    private RegistryTestHost _registry = null!;
    private Guid _arquebusier;

    public async ValueTask InitializeAsync()
    {
        _registry = await RegistryTestHost.StartAsync(postgres, mailpit);
        _arquebusier = (await _registry.RegisterAsync(_registry.Own.Id)).GetProperty("id").GetGuid();
    }

    public async ValueTask DisposeAsync() => await _registry.DisposeAsync();

    [Fact]
    public async Task An_upload_and_a_replacement_record_the_kind_and_whether_it_replaced()
    {
        var first = await UploadAsync("id");
        var second = await UploadAsync("id");

        var entries = await _registry.Host.AuditEntriesAsync("ArquebusierPhotoUploaded");

        Assert.Equal(2, entries.Count);
        Assert.All(entries, entry =>
        {
            Assert.Equal("Arquebusier", entry.EntityType);
            Assert.Equal(_arquebusier.ToString(), entry.EntityId);
            Assert.Equal(_registry.Own.Id, entry.ComparsaId);
            Assert.Equal(_registry.AdminId, entry.ActorUserId);
            AssertNoImageReferences(entry.Data!, first, second);
        });
        var data = entries.OrderBy(e => e.OccurredAt).Select(e => JsonDocument.Parse(e.Data!).RootElement).ToList();
        Assert.Equal(("ID", false), (data[0].GetProperty("kind").GetString(), data[0].GetProperty("replaced").GetBoolean()));
        Assert.Equal(("ID", true), (data[1].GetProperty("kind").GetString(), data[1].GetProperty("replaced").GetBoolean()));
    }

    [Fact]
    public async Task A_removal_records_the_kind()
    {
        var version = await UploadAsync("license-back");

        using var response = await _registry.FiringChief.DeleteAsync(new Uri($"/api/arquebusiers/{_arquebusier}/photos/license-back", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var entry = Assert.Single(await _registry.Host.AuditEntriesAsync("ArquebusierPhotoRemoved"));
        Assert.Equal(_registry.FiringChiefId, entry.ActorUserId);
        Assert.Equal("LICENSE_BACK", JsonDocument.Parse(entry.Data!).RootElement.GetProperty("kind").GetString());
        AssertNoImageReferences(entry.Data!, version);
    }

    [Fact]
    public async Task Rejected_uploads_record_nothing()
    {
        using var invalid = await PhotoRequests.UploadAsync(_registry.Admin, _arquebusier, "id", TestImages.Jpeg(1000, 1000));
        using var outOfScope = await PhotoRequests.UploadAsync(_registry.FiringChief, Guid.CreateVersion7(), "id", TestImages.Jpeg(600, 800));

        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, outOfScope.StatusCode);
        Assert.Empty(await _registry.Host.AuditEntriesAsync("ArquebusierPhotoUploaded"));
    }

    [Fact]
    public async Task The_client_file_name_never_reaches_the_logs()
    {
        await UploadAsync("id");
        using var invalid = await PhotoRequests.UploadAsync(_registry.Admin, _arquebusier, "id", TestImages.PdfHeader(), PersonalFileName);

        Assert.DoesNotContain(_registry.Host.Factory.Logs.Entries, e =>
            e.Message.Contains("12345678Z", StringComparison.Ordinal) || (e.Exception?.Contains("12345678Z", StringComparison.Ordinal) ?? false));
    }

    private static void AssertNoImageReferences(string data, params Guid[] versions)
    {
        Assert.DoesNotContain("registry/photos", data, StringComparison.Ordinal);
        Assert.DoesNotContain("12345678Z", data, StringComparison.Ordinal);
        Assert.DoesNotContain(".jpg", data, StringComparison.Ordinal);
        foreach (var version in versions)
        {
            Assert.DoesNotContain(version.ToString(), data, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(version.ToString("N"), data, StringComparison.OrdinalIgnoreCase);
        }
    }

    private async Task<Guid> UploadAsync(string kind)
    {
        var image = kind == "id" ? TestImages.Jpeg(600, 800) : TestImages.Jpeg(1000, 700);
        using var response = await PhotoRequests.UploadAsync(_registry.Admin, _arquebusier, kind, image, PersonalFileName);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await ReadAsync<JsonElement>(response)).GetProperty("version").GetGuid();
    }
}
