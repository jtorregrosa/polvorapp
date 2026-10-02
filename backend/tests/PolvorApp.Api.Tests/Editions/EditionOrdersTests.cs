using System.Net;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.FestivalEditions.Contracts;
using PolvorApp.IdentityAccess.Contracts;
using static PolvorApp.Api.Tests.Infrastructure.EditionData;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Editions;

/// <summary>Spec "Opening and closing orders (UC-11, BR-10)" and "Current edition" (design D3).</summary>
public sealed class EditionOrdersTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private IdentityTestHost _host = null!;
    private SyntheticUser _adminUser = null!;
    private HttpClient _admin = null!;

    public async ValueTask InitializeAsync()
    {
        _host = await IdentityTestHost.StartAsync(postgres, mailpit);
        _adminUser = await _host.CreateUserAsync("admin.pedidos@example.test", UserRole.Admin);
        _admin = await _host.SignInAsync(_adminUser);
    }

    public async ValueTask DisposeAsync()
    {
        _admin.Dispose();
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task An_admin_opens_closes_and_reopens_the_orders_and_each_change_is_audited()
    {
        var (id, version) = await StartedEditionAsync();

        foreach (var open in new[] { true, false, true })
        {
            using var response = await SetAsync(_admin, id, open, version);
            var edition = await ReadAsync<EditionJson>(response);
            Assert.Equal(open, edition.OrdersOpen);
            version = edition.Version;
        }

        Assert.Equal(2, (await _host.AuditEntriesAsync("EditionOrdersOpened")).Count);
        var closed = Assert.Single(await _host.AuditEntriesAsync("EditionOrdersClosed"));
        Assert.Equal((_adminUser.Id, "FestivalEdition", id.ToString()), (closed.ActorUserId, closed.EntityType, closed.EntityId));
    }

    [Fact]
    public async Task Setting_the_current_value_changes_nothing_and_is_not_audited()
    {
        var (id, version) = await StartedEditionAsync();

        using var response = await SetAsync(_admin, id, false, version);

        Assert.False((await ReadAsync<EditionJson>(response)).OrdersOpen);
        Assert.Empty(await _host.AuditEntriesAsync("EditionOrdersClosed"));
    }

    [Theory]
    [InlineData(EditionStatus.Draft)]
    [InlineData(EditionStatus.Closed)]
    public async Task Only_the_edition_in_progress_opens_its_orders(EditionStatus status)
    {
        var (id, _) = await _admin.CreateCompleteEditionAsync(2031);
        await _host.SetStateAsync(id, status);
        var version = (await _admin.GetEditionAsync(id)).Version;

        using var response = await SetAsync(_admin, id, true, version);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "editions.notInProgress");
        Assert.False((await _admin.GetEditionAsync(id)).OrdersOpen);
    }

    [Fact]
    public async Task An_outdated_change_is_rejected()
    {
        var (id, version) = await StartedEditionAsync();
        using (var first = await SetAsync(_admin, id, true, version))
        {
            first.EnsureSuccessStatusCode();
        }

        using var second = await SetAsync(_admin, id, false, version);

        await AssertProblemAsync(second, HttpStatusCode.Conflict, "editions.modified");
        Assert.True((await _admin.GetEditionAsync(id)).OrdersOpen);
    }

    [Fact]
    public async Task Open_and_version_are_required()
    {
        var (id, _) = await StartedEditionAsync();

        using var response = await _admin.PostAsync($"/api/editions/{id}/orders", new { });

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation");
        Assert.Equal(new Dictionary<string, string> { ["open"] = "required", ["version"] = "required" }, await ErrorsAsync(response));
    }

    [Fact]
    public async Task A_firing_chief_cannot_open_the_orders_but_reads_them_in_the_current_edition()
    {
        var (id, version) = await StartedEditionAsync();
        using var chief = await _host.SignInAsync(await _host.CreateUserAsync("jefe.pedidos@example.test"));

        using var refused = await SetAsync(chief, id, true, version);
        using (var opened = await SetAsync(_admin, id, true, version))
        {
            opened.EnsureSuccessStatusCode();
        }

        using var current = await chief.GetAsync("/api/editions/current", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        var edition = (await ReadAsync<CurrentJson>(current)).Edition;
        Assert.Equal((id, "IN_PROGRESS", true), (edition!.Id, edition.Status, edition.OrdersOpen));
    }

    private async Task<(Guid Id, uint Version)> StartedEditionAsync()
    {
        var (id, version) = await _admin.CreateCompleteEditionAsync(2031);
        using var response = await _admin.PostAsync($"/api/editions/{id}/status", new { status = "IN_PROGRESS", version });
        return (id, (await ReadAsync<EditionJson>(response)).Version);
    }

    private static Task<HttpResponseMessage> SetAsync(HttpClient client, Guid id, bool open, uint version) =>
        client.PostAsync($"/api/editions/{id}/orders", new { open, version });
}
