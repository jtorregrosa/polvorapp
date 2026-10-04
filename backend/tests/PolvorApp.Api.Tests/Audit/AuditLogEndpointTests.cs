using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.AuditPrivacy.Persistence;
using PolvorApp.SharedKernel.Auditing;

namespace PolvorApp.Api.Tests.Audit;

/// <summary>Spec audit-privacy "Audit log query (UC-25)": the audit log endpoints.</summary>
public sealed class AuditLogEndpointTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private RegistryTestHost _registry = null!;

    public async ValueTask InitializeAsync() => _registry = await RegistryTestHost.StartAsync(postgres, mailpit);

    public async ValueTask DisposeAsync() => await _registry.DisposeAsync();

    [Fact]
    public async Task An_admin_reads_the_history_of_a_record_with_names_and_a_link_while_it_exists()
    {
        var arquebusier = await _registry.RegisterAsync(_registry.Own.Id);
        var id = arquebusier.GetProperty("id").GetString()!;

        var before = await ItemsAsync($"?entityType=Arquebusier&entityId={id}");

        var registered = Assert.Single(before);
        Assert.Equal("ArquebusierRegistered", registered.GetProperty("action").GetString());
        Assert.True(registered.GetProperty("recordExists").GetBoolean());
        Assert.Equal("Comparsa Sintética Propia", registered.GetProperty("comparsaName").GetString());
        Assert.Equal(_registry.AdminId, registered.GetProperty("actor").GetProperty("id").GetGuid());
        Assert.False(string.IsNullOrEmpty(registered.GetProperty("actor").GetProperty("name").GetString()));

        _registry.Host.Time.Advance(TimeSpan.FromSeconds(1)); // the host clock is fixed: order the two entries
        using (var deleted = await _registry.Admin.DeleteAsync($"/api/arquebusiers/{id}", TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        }

        var after = await ItemsAsync($"?entityType=Arquebusier&entityId={id}");
        Assert.Equal(["ArquebusierDeleted", "ArquebusierRegistered"], after.Select(e => e.GetProperty("action").GetString()));
        Assert.All(after, e => Assert.False(e.GetProperty("recordExists").GetBoolean()));
    }

    [Fact]
    public async Task Pages_of_fifty_follow_each_other_newest_first()
    {
        await InsertAsync(60, "SignedIn", "User");

        var first = await PageAsync("?action=SignedIn&actorUserId=none");
        var items = first.GetProperty("items").EnumerateArray().ToList();
        var cursor = first.GetProperty("nextCursor").GetString();
        var second = await PageAsync($"?action=SignedIn&actorUserId=none&cursor={Uri.EscapeDataString(cursor!)}");
        var rest = second.GetProperty("items").EnumerateArray().ToList();

        Assert.Equal(50, items.Count);
        Assert.Equal(10, rest.Count);
        Assert.Equal(JsonValueKind.Null, second.GetProperty("nextCursor").ValueKind);
        var times = items.Concat(rest).Select(e => e.GetProperty("occurredAt").GetDateTimeOffset()).ToList();
        Assert.Equal(times.OrderByDescending(t => t), times);
        Assert.Equal(60, items.Concat(rest).Select(e => e.GetProperty("id").GetGuid()).Distinct().Count());
    }

    [Fact]
    public async Task Entries_without_a_user_have_no_actor()
    {
        await InsertAsync(1, "AdminBootstrapRefused", "User");

        var items = await ItemsAsync("?actorUserId=none");

        Assert.NotEmpty(items);
        Assert.All(items, e => Assert.Equal(JsonValueKind.Null, e.GetProperty("actor").ValueKind));
    }

    [Theory]
    [InlineData("?action=Teleported", "action")]
    [InlineData("?from=2030-04-01&to=2030-03-01", "from")]
    [InlineData("?limit=500", "limit")]
    [InlineData("?cursor=nope", "cursor")]
    public async Task An_invalid_filter_is_rejected_naming_it(string query, string field)
    {
        using var response = await _registry.Admin.GetAsync($"/api/audit-entries{query}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.True(problem.GetProperty("errors").TryGetProperty(field, out _), field);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public async Task The_page_size_is_honoured_at_its_bounds(int limit)
    {
        await InsertAsync(101, "SignedIn", "User");

        var items = await ItemsAsync($"?action=SignedIn&actorUserId=none&limit={limit}");

        Assert.Equal(limit, items.Count);
    }

    [Fact]
    public async Task The_recorded_data_and_trace_id_are_returned()
    {
        var arquebusier = await _registry.RegisterAsync(_registry.Own.Id);
        var id = arquebusier.GetProperty("id").GetString()!;
        var edit = Registry.ArquebusierEditingTests.EditOf(arquebusier);
        edit["lastName"] = "Sintética Cambiada";
        using (var updated = await _registry.Admin.PutAsJsonAsync($"/api/arquebusiers/{id}", edit, TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        }

        var entry = Assert.Single(await ItemsAsync($"?entityType=Arquebusier&entityId={id}&action=ArquebusierUpdated"));

        Assert.Contains("lastName", entry.GetProperty("data").GetProperty("changedFields").EnumerateArray().Select(f => f.GetString()));
        Assert.False(string.IsNullOrEmpty(entry.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task An_anonymous_caller_is_refused()
    {
        using var anonymous = await _registry.Host.NewClientAsync();

        using var response = await anonymous.GetAsync("/api/audit-entries", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/audit-entries")]
    [InlineData("/api/audit-entries/actions")]
    public async Task A_firing_chief_is_refused(string path)
    {
        using var response = await _registry.FiringChief.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task The_actions_are_listed_with_their_entity_type()
    {
        using var response = await _registry.Admin.GetAsync("/api/audit-entries/actions", TestContext.Current.CancellationToken);

        var actions = await IdentityAssertions.ReadAsync<JsonElement>(response);
        Assert.Contains(actions.EnumerateArray(), a => a.GetProperty("code").GetString() == "ArquebusierRegistered"
            && a.GetProperty("entityType").GetString() == "Arquebusier");
    }

    [Fact]
    public async Task Reading_the_log_is_not_audited()
    {
        var before = await CountAsync();

        await PageAsync(string.Empty);
        await PageAsync("?entityType=Arquebusier");
        await PageAsync("?actorUserId=none");

        Assert.Equal(before, await CountAsync());
    }

    private async Task<JsonElement> PageAsync(string query)
    {
        using var response = await _registry.Admin.GetAsync($"/api/audit-entries{query}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await IdentityAssertions.ReadAsync<JsonElement>(response);
    }

    private async Task<List<JsonElement>> ItemsAsync(string query) =>
        [.. (await PageAsync(query)).GetProperty("items").EnumerateArray()];

    private async Task InsertAsync(int count, string action, string entityType)
    {
        await using var scope = _registry.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        var start = DateTimeOffset.UtcNow.AddMinutes(-count);
        for (var i = 0; i < count; i++)
        {
            db.Add(new AuditEntry { Id = Guid.CreateVersion7(), OccurredAt = start.AddMinutes(i), Action = action, EntityType = entityType });
        }

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<int> CountAsync()
    {
        await using var scope = _registry.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AuditDbContext>().Set<AuditEntry>().CountAsync(TestContext.Current.CancellationToken);
    }
}
