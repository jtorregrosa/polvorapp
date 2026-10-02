using System.Net;
using System.Net.Http.Json;
using PolvorApp.Api.Tests.Infrastructure;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Registry;

/// <summary>Spec "Registry lock (BR-10, UC-11)": reading, locking and unlocking, and its audit (design D8).</summary>
public sealed class RegistryLockTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private RegistryTestHost _registry = null!;

    public async ValueTask InitializeAsync() => _registry = await RegistryTestHost.StartAsync(postgres, mailpit);

    public async ValueTask DisposeAsync() => await _registry.DisposeAsync();

    [Fact]
    public async Task The_registry_starts_unlocked_and_every_user_can_read_it()
    {
        using var response = await _registry.FiringChief.GetAsync("/api/registry/lock", TestContext.Current.CancellationToken);

        Assert.Equal(new LockJson(false, null), await ReadAsync<LockJson>(response));
    }

    [Fact]
    public async Task An_admin_locks_and_unlocks_and_each_change_is_audited()
    {
        using var locked = await SetAsync(_registry.Admin, true);

        var state = await ReadAsync<LockJson>(locked);
        Assert.True(state.Locked);
        Assert.Equal(_registry.Host.Time.GetUtcNow(), state.ChangedAt);
        var entry = Assert.Single(await _registry.Host.AuditEntriesAsync("RegistryLocked"));
        Assert.Equal((_registry.AdminId, "Registry", (Guid?)null), (entry.ActorUserId, entry.EntityType, entry.ComparsaId));

        using var unlocked = await SetAsync(_registry.Admin, false);

        Assert.False((await ReadAsync<LockJson>(unlocked)).Locked);
        Assert.Single(await _registry.Host.AuditEntriesAsync("RegistryUnlocked"));
        using var read = await _registry.FiringChief.GetAsync("/api/registry/lock", TestContext.Current.CancellationToken);
        Assert.False((await ReadAsync<LockJson>(read)).Locked);
    }

    [Fact]
    public async Task Repeating_the_same_state_changes_nothing_and_is_not_audited()
    {
        using (await SetAsync(_registry.Admin, true))
        {
        }

        using var again = await SetAsync(_registry.Admin, true);
        using var unlockUnlocked = await SetAsync(_registry.Admin, false);
        using var unlockAgain = await SetAsync(_registry.Admin, false);

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Single(await _registry.Host.AuditEntriesAsync("RegistryLocked"));
        Assert.Single(await _registry.Host.AuditEntriesAsync("RegistryUnlocked"));
    }

    [Fact]
    public async Task The_state_is_required()
    {
        using var response = await _registry.Admin.PutAsJsonAsync("/api/registry/lock", new { }, TestContext.Current.CancellationToken);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation");
        Assert.Equal("required", (await ErrorsAsync(response))["locked"]);
    }

    [Fact]
    public async Task A_firing_chief_cannot_lock_or_unlock()
    {
        using var response = await SetAsync(_registry.FiringChief, true);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(await _registry.Host.AuditEntriesAsync("RegistryLocked"));
    }

    internal static Task<HttpResponseMessage> SetAsync(HttpClient client, bool locked) =>
        client.PutAsJsonAsync("/api/registry/lock", new { locked }, TestContext.Current.CancellationToken);

    private sealed record LockJson(bool Locked, DateTimeOffset? ChangedAt);
}
