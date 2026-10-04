using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Platform.Storage;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.Api.Tests.Registry;
using PolvorApp.ArquebusierRegistry.Persistence;
using PolvorApp.SharedKernel.Storage;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Privacy;

/// <summary>
/// Spec audit-privacy "Erasing a person's data" under locks, races and storage failures, and the
/// request size limit of design D11 (review follow-ups of task 9.2).
/// </summary>
public sealed class ErasureRobustnessTests(PostgresFixture postgres, MailpitFixture mailpit, MinioFixture minio) : IAsyncLifetime
{
    private const string Reference = "REQ-ROBUSTA-1";
    private RegistryTestHost _registry = null!;

    public async ValueTask InitializeAsync() =>
        _registry = await RegistryTestHost.StartAsync(
            postgres,
            mailpit,
            services => services.AddSingleton<IObjectStorage>(provider => new UndeletableStorage(provider.GetRequiredService<S3ObjectStorage>())),
            minio.SettingsFor($"robust-{Guid.NewGuid():N}"));

    public async ValueTask DisposeAsync() => await _registry.DisposeAsync();

    [Fact]
    public async Task An_erasure_that_waits_on_a_locked_arquebusier_past_the_timeout_is_busy_and_changes_nothing()
    {
        var person = await _registry.RegisterAsync(_registry.Own.Id);
        var id = person.GetProperty("id").GetGuid();
        await using var holder = _registry.Services.CreateAsyncScope();
        var db = holder.ServiceProvider.GetRequiredService<ArquebusierRegistryDbContext>();

        HttpResponseMessage response;
        await using (var transaction = await db.Database.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            await db.Database.SqlQuery<int>($"SELECT 1 AS \"Value\" FROM registry.arquebusiers WHERE id = {id} FOR UPDATE")
                .ToListAsync(TestContext.Current.CancellationToken);

            // The row stays locked for longer than the erasure's 5 s lock timeout.
            response = await Erase(person.GetProperty("nationalId").GetString()!);
            await transaction.RollbackAsync(TestContext.Current.CancellationToken);
        }

        using (response)
        {
            await AssertProblemAsync(response, HttpStatusCode.ServiceUnavailable, "privacy.busy");
        }

        using var still = await _registry.Admin.GetAsync($"/api/arquebusiers/{id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, still.StatusCode);
        Assert.Empty(await _registry.Host.AuditEntriesAsync("PersonalDataErased"));
    }

    [Fact]
    public async Task An_erasure_racing_the_deletion_of_the_same_arquebusier_never_fails()
    {
        var person = await _registry.RegisterAsync(_registry.Own.Id);
        var id = person.GetProperty("id").GetGuid();

        var erasure = Erase(person.GetProperty("nationalId").GetString()!);
        var deletion = _registry.Admin.DeleteAsync($"/api/arquebusiers/{id}", TestContext.Current.CancellationToken);
        using var erased = await erasure;
        using var deleted = await deletion;

        Assert.Contains(erased.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.NotFound });
        Assert.Contains(deleted.StatusCode, new[] { HttpStatusCode.NoContent, HttpStatusCode.NotFound });
        Assert.True(erased.StatusCode == HttpStatusCode.OK || deleted.StatusCode == HttpStatusCode.NoContent);
        using var gone = await _registry.Admin.GetAsync($"/api/arquebusiers/{id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }

    [Fact]
    public async Task Photos_that_cannot_be_erased_after_the_commit_are_left_to_the_sweep()
    {
        var person = await _registry.RegisterAsync(_registry.Own.Id);
        using (var upload = await PhotoRequests.UploadAsync(_registry.Admin, person.GetProperty("id").GetGuid(), "id", TestImages.Jpeg(600, 800)))
        {
            Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        }

        using var response = await Erase(person.GetProperty("nationalId").GetString()!);

        var result = await ReadAsync<System.Text.Json.JsonElement>(response);
        Assert.Equal(1, result.GetProperty("counts").GetProperty("photosDeleted").GetInt32());
        Assert.Equal(1, result.GetProperty("filesPending").GetInt32());
        Assert.Single(await _registry.Host.AuditEntriesAsync("PersonalDataErased"));
    }

    /// <summary>
    /// Design D11: every GDPR endpoint refuses bodies over 4 KB. The test server does not enforce the
    /// limit (Kestrel does), so the endpoints' metadata is what is checked.
    /// </summary>
    [Fact]
    public void Every_privacy_endpoint_limits_its_body_to_4_KB()
    {
        var endpoints = _registry.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.Contains("/privacy/", StringComparison.Ordinal) == true)
            .ToList();

        Assert.Equal(5, endpoints.Count);
        Assert.All(endpoints, e => Assert.Equal(4096, e.Metadata.GetMetadata<IRequestSizeLimitMetadata>()?.MaxRequestBodySize));
    }

    private Task<HttpResponseMessage> Erase(string nationalId) =>
        _registry.Admin.PostAsJsonAsync("/api/privacy/people/erasure", new { nationalId, reference = Reference }, TestContext.Current.CancellationToken);

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
