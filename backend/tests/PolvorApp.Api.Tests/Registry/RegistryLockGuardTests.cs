using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Platform.Storage;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.Api.Tests.Registry.Import;
using PolvorApp.SharedKernel.Storage;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Registry;

/// <summary>
/// Spec "Registry lock (BR-10, UC-11)", design D8: while the registry is locked, every registry write
/// a FiringChief can reach is refused with <c>409 registry.locked</c> and changes nothing, while
/// Admins keep every write. The route inventory fails for any new FiringChief write route until it
/// is listed here, so a missed write path cannot slip past the lock.
/// </summary>
public sealed partial class RegistryLockGuardTests(PostgresFixture postgres, MailpitFixture mailpit)
{
    /// <summary>Every registry route a FiringChief can write through, reviewed against the lock.</summary>
    private static readonly string[] FiringChiefWrites =
    [
        "POST /api/arquebusiers",
        "PUT /api/arquebusiers/{id:guid}",
        "DELETE /api/arquebusiers/{id:guid}",
        "POST /api/arquebusiers/{id:guid}/owned-weapons",
        "PUT /api/arquebusiers/{id:guid}/owned-weapons/{weaponId:guid}",
        "DELETE /api/arquebusiers/{id:guid}/owned-weapons/{weaponId:guid}",
        "PUT /api/arquebusiers/{id:guid}/photos/{kind}",
        "DELETE /api/arquebusiers/{id:guid}/photos/{kind}",
    ];

    [Fact]
    public async Task Every_firing_chief_write_route_is_reviewed_against_the_lock()
    {
        await using var registry = await RegistryTestHost.StartAsync(postgres, mailpit);

        var writes = registry.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.Contains("arquebusiers", StringComparison.Ordinal) == true)
            .Where(e => !e.Metadata.GetOrderedMetadata<Microsoft.AspNetCore.Authorization.IAuthorizeData>()
                .Any(a => a.Policy == PolvorApp.IdentityAccess.Contracts.AuthorizationPolicies.Admin))
            .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["GET"])
                .Where(m => m is "POST" or "PUT" or "PATCH" or "DELETE")
                .Select(m => $"{m} /{e.RoutePattern.RawText!.Trim('/')}"))
            .Select(WithoutConstraints)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(FiringChiefWrites.Select(WithoutConstraints).Order(StringComparer.Ordinal), writes);
    }

    [Fact]
    public async Task While_locked_every_firing_chief_write_is_refused_and_changes_nothing()
    {
        var puts = new CountingStorage();
        await using var registry = await RegistryTestHost.StartAsync(
            postgres, mailpit, services => services.AddSingleton<IObjectStorage>(provider => puts.Wrap(provider.GetRequiredService<S3ObjectStorage>())));
        var model = RegistryData.NewWeaponModel("ARCABUZ SINTÉTICO BLOQUEADO");
        await registry.Services.SaveCatalogAsync(model);
        var target = (await registry.RegisterAsync(registry.Own.Id)).GetProperty("id").GetGuid();
        var weapon = RegistryData.NewOwnedWeapon(target, model.Id, "SINT-0950");
        await registry.Services.SaveRegistryAsync(weapon);
        using (var upload = await PhotoRequests.UploadAsync(registry.Admin, target, "id", TestImages.Jpeg(600, 800)))
        {
            Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        }

        var ids = new Dictionary<string, Guid> { ["id"] = target, ["weaponId"] = weapon.Id, ["modelId"] = model.Id };
        var before = await DetailAsync(registry, target);
        var storedBefore = puts.Count;
        using (var locked = await RegistryLockTests.SetAsync(registry.Admin, true))
        {
            locked.EnsureSuccessStatusCode();
        }

        using (var register = await registry.FiringChief.PostAsync("/api/arquebusiers", RegistryTestHost.NewArquebusier(registry.Own.Id)))
        {
            await AssertProblemAsync(register, HttpStatusCode.Conflict, "registry.locked");
        }

        var routes = RegistryScopeGuardTests.ArquebusierRoutes(registry.Services.GetRequiredService<EndpointDataSource>())
            .Where(r => !r.AdminOnly && r.Method != "GET")
            .ToList();
        Assert.Equal(FiringChiefWrites.Length - 1, routes.Count);
        foreach (var route in routes)
        {
            using var response = await RegistryScopeGuardTests.SendAsync(registry.FiringChief, registry, route, ids);
            Assert.True(response.StatusCode == HttpStatusCode.Conflict, $"{route} answered {(int)response.StatusCode} while locked.");
            Assert.Equal("registry.locked", await ProblemCodeAsync(response));
        }

        Assert.Equal(before.GetRawText(), (await DetailAsync(registry, target)).GetRawText());
        foreach (var action in new[] { "ArquebusierUpdated", "ArquebusierDeleted", "OwnedWeaponAdded", "OwnedWeaponUpdated", "OwnedWeaponRemoved", "ArquebusierPhotoRemoved" })
        {
            Assert.Empty(await registry.Host.AuditEntriesAsync(action));
        }

        Assert.Single(await registry.Host.AuditEntriesAsync("ArquebusierRegistered"));
        Assert.Equal(storedBefore, puts.Count);
        using var list = await registry.Admin.GetAsync("/api/arquebusiers", TestContext.Current.CancellationToken);
        Assert.Single((await ReadAsync<JsonElement>(list)).EnumerateArray());
    }

    [Fact]
    public async Task While_locked_a_duplicate_registration_reveals_nothing()
    {
        await using var registry = await RegistryTestHost.StartAsync(postgres, mailpit);
        var existing = RegistryTestHost.NewArquebusier(registry.Own.Id);
        using (var first = await registry.Admin.PostAsync("/api/arquebusiers", existing))
        {
            Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        }

        using (await RegistryLockTests.SetAsync(registry.Admin, true))
        {
        }

        using var duplicate = await registry.FiringChief.PostAsync("/api/arquebusiers", existing);

        await AssertProblemAsync(duplicate, HttpStatusCode.Conflict, "registry.locked");
    }

    [Fact]
    public async Task While_locked_firing_chiefs_still_read()
    {
        await using var registry = await RegistryTestHost.StartAsync(postgres, mailpit);
        var target = (await registry.RegisterAsync(registry.Own.Id)).GetProperty("id").GetGuid();
        using (var upload = await PhotoRequests.UploadAsync(registry.Admin, target, "id", TestImages.Jpeg(600, 800)))
        {
            upload.EnsureSuccessStatusCode();
        }

        using (await RegistryLockTests.SetAsync(registry.Admin, true))
        {
        }

        using var list = await registry.FiringChief.GetAsync("/api/arquebusiers", TestContext.Current.CancellationToken);
        using var photo = await registry.FiringChief.GetAsync($"/api/arquebusiers/{target}/photos/id", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal(HttpStatusCode.OK, photo.StatusCode);
    }

    [Fact]
    public async Task While_locked_admins_keep_every_write()
    {
        await using var registry = await RegistryTestHost.StartAsync(postgres, mailpit);
        var target = (await registry.RegisterAsync(registry.Own.Id)).GetProperty("id").GetGuid();
        using (var locked = await RegistryLockTests.SetAsync(registry.Admin, true))
        {
            locked.EnsureSuccessStatusCode();
        }

        var registered = (await registry.RegisterAsync(registry.Own.Id)).GetProperty("id").GetGuid();
        var edit = ArquebusierEditingTests.EditOf(await DetailAsync(registry, target));
        edit["phone"] = "+34 600 000 777";
        using var edited = await registry.Admin.PutAsJsonAsync($"/api/arquebusiers/{target}", edit, TestContext.Current.CancellationToken);
        using var transferred = await registry.Admin.PostAsync($"/api/arquebusiers/{target}/transfer", new { comparsaId = registry.Other.Id });
        using var imported = await ImportRequests.ImportAsync(registry.Admin, registry.Own.Id, ImportWorkbookBuilder.Template().ValidRow().Build());
        using var deleted = await registry.Admin.DeleteAsync($"/api/arquebusiers/{registered}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, transferred.StatusCode);
        Assert.Equal(HttpStatusCode.OK, imported.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
    }

    [Fact]
    public async Task While_locked_admins_keep_changing_weapons_and_photos()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var registry = await RegistryTestHost.StartAsync(postgres, mailpit);
        var model = RegistryData.NewWeaponModel("ARCABUZ SINTÉTICO ADMIN");
        await registry.Services.SaveCatalogAsync(model);
        var target = (await registry.RegisterAsync(registry.Own.Id)).GetProperty("id").GetGuid();
        using (var locked = await RegistryLockTests.SetAsync(registry.Admin, true))
        {
            locked.EnsureSuccessStatusCode();
        }

        // The lock is on: the same write is refused to the FiringChief.
        using (var refused = await registry.FiringChief.PostAsJsonAsync(
            $"/api/arquebusiers/{target}/owned-weapons", new { weaponModelId = model.Id, weaponNumber = "1", ownershipGuideNumber = "SINT-0961" }, ct))
        {
            await AssertProblemAsync(refused, HttpStatusCode.Conflict, "registry.locked");
        }

        using var added = await registry.Admin.PostAsJsonAsync(
            $"/api/arquebusiers/{target}/owned-weapons", new { weaponModelId = model.Id, weaponNumber = "1", ownershipGuideNumber = "SINT-0960" }, ct);
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        var weapon = await ReadAsync<JsonElement>(added);
        var weaponId = weapon.GetProperty("id").GetGuid();
        using var edited = await registry.Admin.PutAsJsonAsync(
            $"/api/arquebusiers/{target}/owned-weapons/{weaponId}",
            new { weaponModelId = model.Id, weaponNumber = "2", ownershipGuideNumber = "SINT-0960", version = weapon.GetProperty("version").GetUInt32() },
            ct);
        using var removedWeapon = await registry.Admin.DeleteAsync($"/api/arquebusiers/{target}/owned-weapons/{weaponId}", ct);
        using var uploaded = await PhotoRequests.UploadAsync(registry.Admin, target, "id", TestImages.Jpeg(600, 800));
        using var removedPhoto = await registry.Admin.DeleteAsync($"/api/arquebusiers/{target}/photos/id", ct);

        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, removedWeapon.StatusCode);
        Assert.Equal(HttpStatusCode.OK, uploaded.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, removedPhoto.StatusCode);
    }

    [Fact]
    public async Task Unlocking_restores_firing_chief_writes()
    {
        await using var registry = await RegistryTestHost.StartAsync(postgres, mailpit);
        using (await RegistryLockTests.SetAsync(registry.Admin, true))
        {
        }

        using (await RegistryLockTests.SetAsync(registry.Admin, false))
        {
        }

        using var response = await registry.FiringChief.PostAsync("/api/arquebusiers", RegistryTestHost.NewArquebusier(registry.Own.Id));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    /// <summary>A route with its parameter constraints dropped, e.g. <c>{kind:regex(...)}</c> as <c>{kind}</c>.</summary>
    private static string WithoutConstraints(string route) => RouteConstraint().Replace(route, "{${name}}");

    [GeneratedRegex(@"\{(?<name>\w+):[^/]*\}")]
    private static partial Regex RouteConstraint();

    private static async Task<JsonElement> DetailAsync(RegistryTestHost registry, Guid id)
    {
        using var response = await registry.Admin.GetAsync($"/api/arquebusiers/{id}", TestContext.Current.CancellationToken);
        return await ReadAsync<JsonElement>(response);
    }

    /// <summary>Counts the images stored, to prove a refused upload stores none.</summary>
    private sealed class CountingStorage
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public IObjectStorage Wrap(IObjectStorage inner) => new Counting(inner, this);

        private sealed class Counting(IObjectStorage inner, CountingStorage owner) : IObjectStorage
        {
            public Task PutAsync(string key, ReadOnlyMemory<byte> content, string contentType, CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref owner._count);
                return inner.PutAsync(key, content, contentType, cancellationToken);
            }

            public Task<StoredObject?> GetAsync(string key, CancellationToken cancellationToken) => inner.GetAsync(key, cancellationToken);

            public Task DeleteAsync(string key, CancellationToken cancellationToken) => inner.DeleteAsync(key, cancellationToken);

            public IAsyncEnumerable<StoredObjectInfo> ListAsync(string prefix, CancellationToken cancellationToken) => inner.ListAsync(prefix, cancellationToken);
        }
    }
}
