using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ArquebusierRegistry.Persistence;
using PolvorApp.SharedKernel.Storage;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Registry;

/// <summary>
/// Design D6, D7 (add-arquebusier-photos): photo writes against concurrent edits and deletions, and
/// a storage outage. Each test uses its own bucket, so it can assert exactly which images exist.
/// </summary>
public sealed class PhotoRaceTests(PostgresFixture postgres, MailpitFixture mailpit, MinioFixture minio) : IAsyncLifetime
{
    private RegistryTestHost _registry = null!;
    private string _bucket = string.Empty;
    private Guid _arquebusier;

    public async ValueTask InitializeAsync()
    {
        _bucket = $"race-{Guid.NewGuid():N}";
        _registry = await RegistryTestHost.StartAsync(postgres, mailpit, settings: minio.SettingsFor(_bucket));
        _arquebusier = (await _registry.RegisterAsync(_registry.Own.Id)).GetProperty("id").GetGuid();
    }

    public async ValueTask DisposeAsync() => await _registry.DisposeAsync();

    [Fact]
    public async Task A_license_photo_never_outlives_a_concurrent_license_removal()
    {
        await using var edit = await BeginAsync();
        await LockAsync(edit, "FOR NO KEY UPDATE");

        // The upload waits for the edit's row lock.
        var upload = PhotoRequests.UploadAsync(_registry.FiringChief, _arquebusier, "license-front", TestImages.Jpeg(1000, 700));
        await WaitUntilBlockedAsync(edit);
        await ExecuteAsync(edit, $"UPDATE registry.arquebusiers SET license_type = NULL, license_pending = false, license_issued_on = NULL, license_expires_on = NULL WHERE id = '{_arquebusier}'");
        await edit.Transaction.CommitAsync(Token);

        using var response = await upload;
        await AssertProblemAsync(response, HttpStatusCode.Conflict, "photos.noLicense");
        Assert.False(await PhotoRowExistsAsync(ArquebusierPhotoKind.LicenseFront));
        Assert.Empty(await minio.ListKeysAsync(_bucket, "registry/photos/"));
    }

    [Fact]
    public async Task An_upload_racing_with_a_deletion_leaves_no_reference_and_no_image()
    {
        await using var deletion = await BeginAsync();
        await LockAsync(deletion, "FOR UPDATE");

        var upload = PhotoRequests.UploadAsync(_registry.FiringChief, _arquebusier, "id", TestImages.Jpeg(600, 800));
        await WaitUntilBlockedAsync(deletion);
        await ExecuteAsync(deletion, $"DELETE FROM registry.arquebusiers WHERE id = '{_arquebusier}'");
        await deletion.Transaction.CommitAsync(Token);

        using var response = await upload;
        await AssertProblemAsync(response, HttpStatusCode.NotFound, "arquebusiers.notFound");
        Assert.Empty(await minio.ListKeysAsync(_bucket, "registry/photos/"));
    }

    [Fact]
    public async Task Two_first_uploads_of_the_same_kind_give_one_photo_and_one_conflict()
    {
        // Another first upload has inserted its row and not committed yet.
        await using var other = await BeginAsync();
        await LockAsync(other, "FOR SHARE");
        var otherId = Guid.CreateVersion7();
        await ExecuteAsync(other, $"INSERT INTO registry.arquebusier_photos (id, arquebusier_id, kind, object_key, width, height, size_bytes, uploaded_at) VALUES ('{otherId}', '{_arquebusier}', 'ID', 'registry/photos/{otherId:N}.jpg', 600, 800, 1000, now())");

        var upload = PhotoRequests.UploadAsync(_registry.FiringChief, _arquebusier, "id", TestImages.Jpeg(600, 800));
        await WaitUntilBlockedAsync(other);
        await other.Transaction.CommitAsync(Token);

        using var response = await upload;
        await AssertProblemAsync(response, HttpStatusCode.Conflict, "photos.modified");
        Assert.Empty(await minio.ListKeysAsync(_bucket, "registry/photos/"));
    }

    [Fact]
    public async Task Two_concurrent_replacements_both_succeed_and_the_last_one_wins()
    {
        Guid original;
        using (var first = await PhotoRequests.UploadAsync(_registry.Admin, _arquebusier, "id", TestImages.Jpeg(600, 800)))
        {
            original = (await ReadAsync<JsonElement>(first)).GetProperty("version").GetGuid();
        }

        // Another replacement has stored its image, holds the photo row and has rewritten it, not committed yet.
        await using var other = await BeginAsync();
        await LockAsync(other, "FOR SHARE");
        var otherId = Guid.CreateVersion7();
        using var s3 = minio.CreateClient();
        await s3.PutObjectAsync(new Amazon.S3.Model.PutObjectRequest { BucketName = _bucket, Key = $"registry/photos/{otherId:N}.jpg", ContentBody = "other" }, Token);
        await ExecuteAsync(other, $"SELECT 1 FROM registry.arquebusier_photos WHERE arquebusier_id = '{_arquebusier}' AND kind = 'ID' FOR UPDATE");
        await ExecuteAsync(other, $"UPDATE registry.arquebusier_photos SET id = '{otherId}', object_key = 'registry/photos/{otherId:N}.jpg' WHERE arquebusier_id = '{_arquebusier}' AND kind = 'ID'");

        var upload = PhotoRequests.UploadAsync(_registry.FiringChief, _arquebusier, "id", TestImages.Jpeg(600, 800));
        await WaitUntilBlockedAsync(other);
        await other.Transaction.CommitAsync(Token);

        // The other request then erases the image it replaced.
        await s3.DeleteObjectAsync(_bucket, $"registry/photos/{original:N}.jpg", Token);

        using var response = await upload;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var winner = (await ReadAsync<JsonElement>(response)).GetProperty("version").GetGuid();
        await using var scope = _registry.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ArquebusierRegistryDbContext>();
        Assert.Equal(winner, (await db.Photos.SingleAsync(p => p.ArquebusierId == _arquebusier, Token)).Id);
        Assert.Equal([$"registry/photos/{winner:N}.jpg"], await minio.ListKeysAsync(_bucket, "registry/photos/"));
    }

    [Fact]
    public async Task The_registry_owner_reports_exactly_the_referenced_keys()
    {
        using var first = await PhotoRequests.UploadAsync(_registry.Admin, _arquebusier, "id", TestImages.Jpeg(600, 800));
        var key = $"registry/photos/{(await ReadAsync<JsonElement>(first)).GetProperty("version").GetGuid():N}.jpg";
        await using var scope = _registry.Services.CreateAsyncScope();
        var owner = scope.ServiceProvider.GetServices<IStoredObjectOwner>().Single(o => o.Prefix == "registry/photos/");

        var referenced = await owner.FilterReferencedAsync([key, key.ToUpperInvariant(), "registry/photos/other.jpg"], Token);

        Assert.Equal([key], referenced);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>The configured connection string: the context's copy no longer carries the password.</summary>
    private string ConnectionString => _registry.Services.GetRequiredService<IConfiguration>()["ConnectionStrings:Postgres"]!;

    private async Task<OpenTransaction> BeginAsync()
    {
        var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(Token);
        return new OpenTransaction(connection, await connection.BeginTransactionAsync(Token));
    }

    /// <summary>Waits until another session of this database waits for a lock that <paramref name="open"/> holds.</summary>
    private async Task WaitUntilBlockedAsync(OpenTransaction open)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            await using var probe = new NpgsqlConnection(ConnectionString);
            await probe.OpenAsync(Token);
            await using var command = new NpgsqlCommand(
                "SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock' AND @holder = ANY(pg_blocking_pids(pid))",
                probe);
            command.Parameters.AddWithValue("holder", open.Connection.ProcessID);
            if ((long)(await command.ExecuteScalarAsync(Token))! > 0)
            {
                return;
            }

            await Task.Delay(20, Token);
        }

        Assert.Fail("The upload never waited for the concurrent transaction.");
    }

    private async Task LockAsync(OpenTransaction open, string mode) =>
        await ExecuteAsync(open, $"SELECT 1 FROM registry.arquebusiers WHERE id = '{_arquebusier}' {mode}");

    private static async Task ExecuteAsync(OpenTransaction open, string sql)
    {
        await using var command = new NpgsqlCommand(sql, open.Connection, open.Transaction);
        await command.ExecuteNonQueryAsync(Token);
    }

    private async Task<bool> PhotoRowExistsAsync(ArquebusierPhotoKind kind)
    {
        await using var scope = _registry.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ArquebusierRegistryDbContext>();
        return await db.Photos.AnyAsync(p => p.ArquebusierId == _arquebusier && p.Kind == kind, Token);
    }

    private sealed record OpenTransaction(NpgsqlConnection Connection, NpgsqlTransaction Transaction) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Transaction.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }
}

/// <summary>Spec "Private photo access": a storage outage blocks photo operations only.</summary>
public sealed class PhotoOutageTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private RegistryTestHost _registry = null!;
    private JsonElement _arquebusier;
    private Guid _photo;

    public async ValueTask InitializeAsync()
    {
        _registry = await RegistryTestHost.StartAsync(postgres, mailpit, services => services.AddSingleton<IObjectStorage, OutageStorage>());
        _arquebusier = await _registry.RegisterAsync(_registry.Own.Id);
        var photo = RegistryData.NewPhoto(Id, ArquebusierPhotoKind.Id);
        await _registry.Services.SaveRegistryAsync(photo);
        _photo = photo.Id;
    }

    public async ValueTask DisposeAsync() => await _registry.DisposeAsync();

    private Guid Id => _arquebusier.GetProperty("id").GetGuid();

    [Fact]
    public async Task Photo_operations_answer_service_unavailable()
    {
        using var upload = await PhotoRequests.UploadAsync(_registry.FiringChief, Id, "id", TestImages.Jpeg(600, 800));
        using var read = await _registry.FiringChief.GetAsync(new Uri($"/api/arquebusiers/{Id}/photos/id", UriKind.Relative), TestContext.Current.CancellationToken);

        await AssertProblemAsync(upload, HttpStatusCode.ServiceUnavailable, "storage.unavailable");
        await AssertProblemAsync(read, HttpStatusCode.ServiceUnavailable, "storage.unavailable");
        // Nothing is changed: the existing photo is still the one referenced.
        var detail = await DetailAsync();
        Assert.Equal(_photo, detail.GetProperty("photos").GetProperty("id").GetProperty("version").GetGuid());
        Assert.Equal(_arquebusier.GetProperty("version").GetUInt32(), detail.GetProperty("version").GetUInt32());
    }

    /// <summary>
    /// Design D2: a removal commits first and erases after, so it needs no storage; the image is left
    /// to the sweep (BR-14 within its 24 h).
    /// </summary>
    [Fact]
    public async Task Removing_a_photo_still_works_and_leaves_the_image_to_the_sweep()
    {
        using var removal = await _registry.FiringChief.DeleteAsync(new Uri($"/api/arquebusiers/{Id}/photos/id", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, removal.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await DetailAsync()).GetProperty("photos").GetProperty("id").ValueKind);
    }

    private async Task<JsonElement> DetailAsync()
    {
        using var response = await _registry.Admin.GetAsync(new Uri($"/api/arquebusiers/{Id}", UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadAsync<JsonElement>(response);
    }

    [Fact]
    public async Task Editing_transferring_and_deleting_still_work()
    {
        var body = RegistryTestHost.NewArquebusier(_registry.Own.Id);
        body.Remove("comparsaId");
        (body["nationalId"], body["federationId"]) = (_arquebusier.GetProperty("nationalId").GetString(), _arquebusier.GetProperty("federationId").GetInt32());
        body["status"] = "RESERVE";
        body["version"] = _arquebusier.GetProperty("version").GetUInt32();
        using var edit = await _registry.FiringChief.PutAsync(
            new Uri($"/api/arquebusiers/{Id}", UriKind.Relative), System.Net.Http.Json.JsonContent.Create(body), TestContext.Current.CancellationToken);
        using var transfer = await _registry.Admin.PostAsync($"/api/arquebusiers/{Id}/transfer", new { comparsaId = _registry.Other.Id });
        using var deletion = await _registry.Admin.DeleteAsync(new Uri($"/api/arquebusiers/{Id}", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, transfer.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, deletion.StatusCode);
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
