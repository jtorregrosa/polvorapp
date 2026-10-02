using System.Net;
using System.Text.Json;
using PolvorApp.Api.Tests.Infrastructure;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Catalog;

/// <summary>Spec "Catalogue changes are audited" (logo writes; design D7).</summary>
public sealed class ComparsaLogoAuditTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private RegistryTestHost _host = null!;

    public async ValueTask InitializeAsync() => _host = await RegistryTestHost.StartAsync(postgres, mailpit);

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task Uploads_say_whether_they_replaced_a_logo()
    {
        await UploadAsync();
        await UploadAsync();

        var entries = await _host.Host.AuditEntriesAsync("ComparsaLogoUploaded");

        Assert.Equal(2, entries.Count);
        Assert.All(entries, entry => Assert.Equal(
            (_host.AdminId, _host.Own.Id, _host.Own.Id.ToString(), "Comparsa"),
            (entry.ActorUserId, entry.ComparsaId, entry.EntityId, entry.EntityType)));
        // The test clock does not move between the two: compare the set, not the order.
        Assert.Equal([false, true], entries.Select(e => Replaced(e.Data)).Order());
    }

    [Fact]
    public async Task A_removal_is_audited()
    {
        await UploadAsync();

        using var removal = await _host.Admin.DeleteAsync(LogoRequests.Uri(_host.Own.Id), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, removal.StatusCode);
        var entry = Assert.Single(await _host.Host.AuditEntriesAsync("ComparsaLogoRemoved"));
        Assert.Equal((_host.AdminId, _host.Own.Id), (entry.ActorUserId, entry.ComparsaId));
    }

    [Fact]
    public async Task A_rejected_upload_records_nothing()
    {
        using var response = await LogoRequests.UploadAsync(_host.Admin, _host.Own.Id, TestImages.Png(100, 100));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await _host.Host.AuditEntriesAsync("ComparsaLogoUploaded"));
    }

    [Fact]
    public async Task An_entry_never_holds_a_key_a_size_or_dimensions()
    {
        var version = await UploadAsync();

        var data = Assert.Single(await _host.Host.AuditEntriesAsync("ComparsaLogoUploaded")).Data!;

        using var document = JsonDocument.Parse(data);
        Assert.Equal(["replaced"], document.RootElement.EnumerateObject().Select(p => p.Name));
        Assert.DoesNotContain(version.ToString("N"), data, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("catalog/logos", data, StringComparison.Ordinal);
    }

    private static bool Replaced(string? data)
    {
        using var document = JsonDocument.Parse(data!);
        return document.RootElement.GetProperty("replaced").GetBoolean();
    }

    private async Task<Guid> UploadAsync()
    {
        using var response = await LogoRequests.UploadAsync(_host.Admin, _host.Own.Id, TestImages.Png(800, 400));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await ReadAsync<JsonElement>(response)).GetProperty("version").GetGuid();
    }
}
