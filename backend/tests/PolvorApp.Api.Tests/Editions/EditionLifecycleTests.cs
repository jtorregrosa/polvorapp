using System.Net;
using System.Text.Json;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.FestivalEditions.Contracts;
using PolvorApp.IdentityAccess.Contracts;
using static PolvorApp.Api.Tests.Infrastructure.EditionData;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Editions;

/// <summary>Spec "Edition lifecycle (UC-11)" and its audit (design D3): moves, completeness, one edition in progress.</summary>
public sealed class EditionLifecycleTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private IdentityTestHost _host = null!;
    private SyntheticUser _adminUser = null!;
    private HttpClient _admin = null!;

    public async ValueTask InitializeAsync()
    {
        _host = await IdentityTestHost.StartAsync(postgres, mailpit);
        _adminUser = await _host.CreateUserAsync("admin.ciclo@example.test", UserRole.Admin);
        _admin = await _host.SignInAsync(_adminUser);
    }

    public async ValueTask DisposeAsync()
    {
        _admin.Dispose();
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task An_admin_starts_a_complete_draft_and_it_is_audited_with_both_statuses()
    {
        var (id, version) = await _admin.CreateCompleteEditionAsync(2031);

        using var response = await MoveAsync(_admin, id, "IN_PROGRESS", version);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var edition = await ReadAsync<EditionJson>(response);
        Assert.Equal(("IN_PROGRESS", false), (edition.Status, edition.OrdersOpen));
        var entry = Assert.Single(await _host.AuditEntriesAsync("EditionStatusChanged"));
        Assert.Equal((_adminUser.Id, id.ToString()), (entry.ActorUserId, entry.EntityId));
        using var data = JsonDocument.Parse(entry.Data!);
        Assert.Equal(("DRAFT", "IN_PROGRESS"), (data.RootElement.GetProperty("previous").GetString(), data.RootElement.GetProperty("current").GetString()));
    }

    [Fact]
    public async Task Every_one_step_move_forward_and_back_is_allowed()
    {
        var (id, version) = await _admin.CreateCompleteEditionAsync(2031);

        foreach (var status in new[] { "IN_PROGRESS", "CLOSED", "IN_PROGRESS", "DRAFT" })
        {
            using var response = await MoveAsync(_admin, id, status, version);
            var edition = await ReadAsync<EditionJson>(response);
            Assert.Equal(status, edition.Status);
            version = edition.Version;
        }

        Assert.Equal(4, (await _host.AuditEntriesAsync("EditionStatusChanged")).Count);
    }

    [Fact]
    public async Task Skipping_a_step_is_blocking_and_not_audited()
    {
        var (id, version) = await _admin.CreateCompleteEditionAsync(2031);

        using var response = await MoveAsync(_admin, id, "CLOSED", version);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "editions.invalidTransition");
        Assert.Equal("DRAFT", (await _admin.GetEditionAsync(id)).Status);
        Assert.Empty(await _host.AuditEntriesAsync("EditionStatusChanged"));
    }

    [Fact]
    public async Task An_incomplete_draft_cannot_start_and_the_missing_fields_are_listed()
    {
        var (id, version) = await _admin.CreateEditionAsync(2031);

        using var response = await MoveAsync(_admin, id, "IN_PROGRESS", version);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "editions.incomplete");
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(
            ["ordersOpenOn", "ordersCloseOn", "prices.powderPerKg", "prices.capsBox", "prices.weaponRental", "prices.flaskRental"],
            problem.RootElement.GetProperty("missing").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task Leaving_the_edition_in_progress_needs_its_orders_closed()
    {
        var (id, _) = await _admin.CreateCompleteEditionAsync(2031);
        await _host.SetStateAsync(id, EditionStatus.InProgress, ordersOpen: true);
        var version = (await _admin.GetEditionAsync(id)).Version;

        using var close = await MoveAsync(_admin, id, "CLOSED", version);
        using var back = await MoveAsync(_admin, id, "DRAFT", version);

        await AssertProblemAsync(close, HttpStatusCode.Conflict, "editions.ordersOpen");
        await AssertProblemAsync(back, HttpStatusCode.Conflict, "editions.ordersOpen");
        Assert.Equal("IN_PROGRESS", (await _admin.GetEditionAsync(id)).Status);
    }

    [Fact]
    public async Task A_second_edition_in_progress_is_blocking_and_names_the_one_in_progress()
    {
        var (current, _) = await _admin.CreateCompleteEditionAsync(2031);
        await _host.SetStateAsync(current, EditionStatus.InProgress);
        var (next, version) = await _admin.CreateCompleteEditionAsync(2032);

        using var response = await MoveAsync(_admin, next, "IN_PROGRESS", version);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "editions.anotherInProgress");
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2031, problem.RootElement.GetProperty("inProgressYear").GetInt32());
    }

    [Fact]
    public async Task Reopening_a_closed_edition_while_another_is_in_progress_is_blocking()
    {
        var (closed, _) = await _admin.CreateCompleteEditionAsync(2030);
        await _host.SetStateAsync(closed, EditionStatus.Closed);
        var (current, _) = await _admin.CreateCompleteEditionAsync(2031);
        await _host.SetStateAsync(current, EditionStatus.InProgress);
        var version = (await _admin.GetEditionAsync(closed)).Version;

        using var response = await MoveAsync(_admin, closed, "IN_PROGRESS", version);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "editions.anotherInProgress");
    }

    [Fact]
    public async Task An_outdated_move_is_rejected()
    {
        var (id, version) = await _admin.CreateCompleteEditionAsync(2031);
        using (var first = await MoveAsync(_admin, id, "IN_PROGRESS", version))
        {
            first.EnsureSuccessStatusCode();
        }

        using var second = await MoveAsync(_admin, id, "CLOSED", version);

        await AssertProblemAsync(second, HttpStatusCode.Conflict, "editions.modified");
    }

    [Theory]
    [InlineData(null, "required")]
    [InlineData("LOCKED", "invalid")]
    public async Task An_invalid_target_status_is_named(string? status, string reason)
    {
        var (id, version) = await _admin.CreateCompleteEditionAsync(2031);

        using var response = await _admin.PostAsync($"/api/editions/{id}/status", new { status, version });

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation");
        Assert.Equal(reason, (await ErrorsAsync(response))["status"]);
    }

    [Fact]
    public async Task Moving_an_unknown_edition_is_not_found()
    {
        using var response = await MoveAsync(_admin, Guid.CreateVersion7(), "IN_PROGRESS", 1);

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "editions.notFound");
    }

    [Fact]
    public async Task A_firing_chief_cannot_change_the_status()
    {
        var (id, version) = await _admin.CreateCompleteEditionAsync(2031);
        using var chief = await _host.SignInAsync(await _host.CreateUserAsync("jefe.ciclo@example.test"));

        using var response = await MoveAsync(chief, id, "IN_PROGRESS", version);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("DRAFT", (await _admin.GetEditionAsync(id)).Status);
    }

    [Fact]
    public async Task Two_editions_started_at_once_leave_exactly_one_in_progress()
    {
        BarrierAuditTrail barrier = null!;
        await using var host = await IdentityTestHost.StartAsync(
            postgres, mailpit, configureServices: services => barrier = BarrierAuditTrail.Decorate(services, "EditionStatusChanged", parties: 2));
        using var admin = await host.SignInAsync(await host.CreateUserAsync("admin.carrera@example.test", UserRole.Admin));
        var first = await admin.CreateCompleteEditionAsync(2031);
        var second = await admin.CreateCompleteEditionAsync(2032);

        var responses = await Task.WhenAll(new[] { first, second }.Select(e => MoveAsync(admin, e.Id, "IN_PROGRESS", e.Version)));

        Assert.Equal(2, barrier.Arrived);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        await AssertProblemAsync(Assert.Single(responses, r => r.StatusCode != HttpStatusCode.OK), HttpStatusCode.Conflict, "editions.anotherInProgress");
        Assert.Single(await host.AuditEntriesAsync("EditionStatusChanged"));
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    private static Task<HttpResponseMessage> MoveAsync(HttpClient client, Guid id, string status, uint version) =>
        client.PostAsync($"/api/editions/{id}/status", new { status, version });
}
