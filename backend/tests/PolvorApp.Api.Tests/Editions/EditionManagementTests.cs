using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.FestivalEditions.Contracts;
using PolvorApp.IdentityAccess.Contracts;
using static PolvorApp.Api.Tests.Infrastructure.EditionData;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Editions;

/// <summary>
/// Specs "Festival editions (UC-10)", "Edition prices", "New editions start from the previous one",
/// "Edition management by Admins", "Edition visibility (BR-12)" and their audit (design D4–D6).
/// </summary>
public sealed class EditionManagementTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private IdentityTestHost _host = null!;
    private SyntheticUser _adminUser = null!;
    private HttpClient _admin = null!;
    private HttpClient _chief = null!;

    public async ValueTask InitializeAsync()
    {
        _host = await IdentityTestHost.StartAsync(postgres, mailpit);
        _adminUser = await _host.CreateUserAsync("admin.ediciones@example.test", UserRole.Admin);
        _admin = await _host.SignInAsync(_adminUser);
        _chief = await _host.SignInAsync(await _host.CreateUserAsync("jefe.ediciones@example.test"));
    }

    public async ValueTask DisposeAsync()
    {
        _admin.Dispose();
        _chief.Dispose();
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task An_admin_creates_the_first_edition_as_an_empty_draft()
    {
        using var response = await _admin.PostAsync("/api/editions", new { year = 2031, festivalStartsOn = "2031-04-22", festivalEndsOn = "2031-04-25" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var edition = await ReadAsync<EditionJson>(response);
        Assert.Equal($"/api/editions/{edition.Id}", response.Headers.Location?.OriginalString);
        Assert.Equal((2031, "DRAFT", false), (edition.Year, edition.Status, edition.OrdersOpen));
        Assert.Equal((new DateOnly(2031, 4, 22), new DateOnly(2031, 4, 25)), (edition.FestivalStartsOn, edition.FestivalEndsOn));
        Assert.Equal((null, null), (edition.OrdersOpenOn, edition.OrdersCloseOn));
        Assert.Equal(new PricesJson(null, null, null, null), edition.Prices);
        Assert.Empty(edition.WeaponModels);
        Assert.Empty(edition.Milestones);

        var entry = Assert.Single(await _host.AuditEntriesAsync("EditionCreated"));
        Assert.Equal((_adminUser.Id, "FestivalEdition", edition.Id.ToString(), (Guid?)null), (entry.ActorUserId, entry.EntityType, entry.EntityId, entry.ComparsaId));
        using var data = JsonDocument.Parse(entry.Data!);
        Assert.Equal(JsonValueKind.Null, data.RootElement.GetProperty("copiedFrom").ValueKind);
    }

    [Fact]
    public async Task A_new_edition_copies_the_prices_and_still_rentable_models_of_the_latest_earlier_one()
    {
        await _admin.CreateEditionAsync(2029);
        var (previous, version) = await _admin.CreateEditionAsync(2031);
        using (var complete = await _admin.PutAsJsonAsync($"/api/editions/{previous}", CompleteBody(2031, version), TestContext.Current.CancellationToken))
        {
            complete.EnsureSuccessStatusCode();
        }

        var kept = await _admin.CreateRentableModelAsync("ARCABUZ COPIADO DIESTRO");
        var alsoKept = await _admin.CreateRentableModelAsync("ARCABUZ COPIADO ZURDO", handedness: "LEFT");
        var retired = await _admin.CreateRentableModelAsync("ARCABUZ RETIRADO", size: "SMALL");
        await _host.OfferAsync(previous, kept, alsoKept, retired);
        using (var deactivate = await _admin.PostAsync($"/api/weapon-models/{retired}/deactivate", new { }))
        {
            deactivate.EnsureSuccessStatusCode();
        }

        using var response = await _admin.PostAsync("/api/editions", new { year = 2032, festivalStartsOn = "2032-04-20", festivalEndsOn = "2032-04-23" });

        var edition = await ReadAsync<EditionJson>(response);
        Assert.Equal(new PricesJson(55.00m, 4.50m, 30.00m, 6.00m), edition.Prices);
        Assert.Equal(new[] { kept, alsoKept }.Order(), edition.WeaponModels.Select(m => m.Id).Order());
        Assert.Equal((null, null), (edition.OrdersOpenOn, edition.OrdersCloseOn));
        Assert.Empty(edition.Milestones);
        var entry = (await _host.AuditEntriesAsync("EditionCreated")).Single(e => e.EntityId == edition.Id.ToString());
        using var data = JsonDocument.Parse(entry.Data!);
        Assert.Equal(2031, data.RootElement.GetProperty("copiedFrom").GetInt32());
    }

    [Fact]
    public async Task An_edition_before_every_other_starts_empty()
    {
        var (later, version) = await _admin.CreateEditionAsync(2031);
        using (var complete = await _admin.PutAsJsonAsync($"/api/editions/{later}", CompleteBody(2031, version), TestContext.Current.CancellationToken))
        {
            complete.EnsureSuccessStatusCode();
        }

        var (earlier, _) = await _admin.CreateEditionAsync(2025);

        Assert.Equal(new PricesJson(null, null, null, null), (await _admin.GetEditionAsync(earlier)).Prices);
    }

    [Fact]
    public async Task A_duplicate_year_is_blocking()
    {
        await _admin.CreateEditionAsync(2031);

        using var response = await _admin.PostAsync("/api/editions", new { year = 2031, festivalStartsOn = "2031-04-20", festivalEndsOn = "2031-04-21" });

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "editions.yearTaken");
        Assert.Single(await _host.AuditEntriesAsync("EditionCreated"));
    }

    [Fact]
    public async Task Invalid_create_fields_are_named()
    {
        using var noYear = await _admin.PostAsync("/api/editions", new { festivalStartsOn = "2031-04-22" });
        using var outside = await _admin.PostAsync("/api/editions", new { year = 2031, festivalStartsOn = "2030-12-30", festivalEndsOn = "2031-04-25" });
        using var range = await _admin.PostAsync("/api/editions", new { year = 1999, festivalStartsOn = "1999-04-22", festivalEndsOn = "1999-04-25" });

        await AssertProblemAsync(noYear, HttpStatusCode.BadRequest, "validation");
        Assert.Equal(new Dictionary<string, string> { ["year"] = "required", ["festivalEndsOn"] = "required" }, await ErrorsAsync(noYear));
        Assert.Equal(new Dictionary<string, string> { ["festivalStartsOn"] = "outsideYear" }, await ErrorsAsync(outside));
        Assert.Equal(new Dictionary<string, string> { ["year"] = "outOfRange" }, await ErrorsAsync(range));
    }

    [Fact]
    public async Task An_admin_edits_the_dates_and_prices_and_only_the_changes_are_audited()
    {
        var (id, version) = await _admin.CreateEditionAsync(2031);

        using var response = await _admin.PutAsJsonAsync($"/api/editions/{id}", CompleteBody(2031, version), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var edition = await ReadAsync<EditionJson>(response);
        Assert.Equal(new PricesJson(55.00m, 4.50m, 30.00m, 6.00m), edition.Prices);
        Assert.Equal((new DateOnly(2031, 1, 10), new DateOnly(2031, 2, 10)), (edition.OrdersOpenOn, edition.OrdersCloseOn));
        Assert.NotEqual(version, edition.Version);
        var entry = Assert.Single(await _host.AuditEntriesAsync("EditionUpdated"));
        using var data = JsonDocument.Parse(entry.Data!);
        // jsonb stores object keys in its own order, so compare them as a set.
        var changed = data.RootElement.GetProperty("current").EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(["ordersOpenOn", "ordersCloseOn", "prices.powderPerKg", "prices.capsBox", "prices.weaponRental", "prices.flaskRental"], changed);
        Assert.Equal(JsonValueKind.Null, data.RootElement.GetProperty("previous").GetProperty("prices.capsBox").ValueKind);
    }

    [Fact]
    public async Task Prices_are_returned_with_two_decimals()
    {
        var (id, version) = await _admin.CreateEditionAsync(2031);
        using var response = await _admin.PutAsJsonAsync($"/api/editions/{id}", CompleteBody(2031, version, capsBox: 4.5m), TestContext.Current.CancellationToken);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Contains("\"capsBox\":4.50", body, StringComparison.Ordinal);
        Assert.Contains("\"weaponRental\":30.00", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Saving_without_changes_succeeds_without_an_audit_entry()
    {
        var (id, version) = await _admin.CreateCompleteEditionAsync(2031);

        using var response = await _admin.PutAsJsonAsync($"/api/editions/{id}", CompleteBody(2031, version), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(await _host.AuditEntriesAsync("EditionUpdated"));
    }

    [Fact]
    public async Task An_outdated_edit_is_rejected()
    {
        var (id, version) = await _admin.CreateEditionAsync(2031);
        using (var first = await _admin.PutAsJsonAsync($"/api/editions/{id}", CompleteBody(2031, version), TestContext.Current.CancellationToken))
        {
            first.EnsureSuccessStatusCode();
        }

        using var second = await _admin.PutAsJsonAsync($"/api/editions/{id}", CompleteBody(2031, version, capsBox: 5m), TestContext.Current.CancellationToken);

        await AssertProblemAsync(second, HttpStatusCode.Conflict, "editions.modified");
        Assert.Equal(4.50m, (await _admin.GetEditionAsync(id)).Prices.CapsBox);
    }

    [Fact]
    public async Task An_edit_needs_the_version()
    {
        var (id, _) = await _admin.CreateEditionAsync(2031);

        using var response = await _admin.PutAsJsonAsync($"/api/editions/{id}", new { festivalStartsOn = "2031-04-22", festivalEndsOn = "2031-04-25" }, TestContext.Current.CancellationToken);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation");
        Assert.Equal("required", (await ErrorsAsync(response))["version"]);
    }

    [Fact]
    public async Task Invalid_edit_fields_are_named_and_nothing_changes()
    {
        var (id, version) = await _admin.CreateEditionAsync(2031);

        using var response = await _admin.PutAsJsonAsync(
            $"/api/editions/{id}",
            new { festivalStartsOn = "2031-04-22", festivalEndsOn = "2031-04-25", ordersOpenOn = "2031-02-10", ordersCloseOn = "2031-02-01", prices = new { capsBox = 4.555m }, version },
            TestContext.Current.CancellationToken);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation");
        Assert.Equal(new Dictionary<string, string> { ["ordersCloseOn"] = "beforeOpen", ["prices.capsBox"] = "decimals" }, await ErrorsAsync(response));
        Assert.Empty(await _host.AuditEntriesAsync("EditionUpdated"));
    }

    [Theory]
    [InlineData(EditionStatus.InProgress)]
    [InlineData(EditionStatus.Closed)]
    public async Task Admins_edit_an_edition_in_any_status(EditionStatus status)
    {
        var (id, _) = await _admin.CreateCompleteEditionAsync(2031);
        await _host.SetStateAsync(id, status);
        var version = (await _admin.GetEditionAsync(id)).Version;

        using var response = await _admin.PutAsJsonAsync($"/api/editions/{id}", CompleteBody(2031, version, capsBox: 5m), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(5.00m, (await ReadAsync<EditionJson>(response)).Prices.CapsBox);
    }

    [Fact]
    public async Task Outside_a_draft_the_window_and_prices_stay_required()
    {
        var (id, _) = await _admin.CreateCompleteEditionAsync(2031);
        await _host.SetStateAsync(id, EditionStatus.InProgress);
        var version = (await _admin.GetEditionAsync(id)).Version;

        using var response = await _admin.PutAsJsonAsync(
            $"/api/editions/{id}",
            new { festivalStartsOn = "2031-04-22", festivalEndsOn = "2031-04-25", ordersOpenOn = "2031-01-10", prices = new { powderPerKg = 55m, capsBox = 4.5m, weaponRental = 30m }, version },
            TestContext.Current.CancellationToken);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation");
        Assert.Equal(new Dictionary<string, string> { ["ordersCloseOn"] = "required", ["prices.flaskRental"] = "required" }, await ErrorsAsync(response));
    }

    [Fact]
    public async Task An_admin_deletes_a_draft_and_it_is_audited_with_a_snapshot()
    {
        var (id, _) = await _admin.CreateCompleteEditionAsync(2031);

        using var response = await _admin.DeleteAsync($"/api/editions/{id}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using var gone = await _admin.GetAsync($"/api/editions/{id}", TestContext.Current.CancellationToken);
        await AssertProblemAsync(gone, HttpStatusCode.NotFound, "editions.notFound");
        var entry = Assert.Single(await _host.AuditEntriesAsync("EditionDeleted"));
        using var data = JsonDocument.Parse(entry.Data!);
        Assert.Equal(2031, data.RootElement.GetProperty("year").GetInt32());
        Assert.Equal("DRAFT", data.RootElement.GetProperty("status").GetString());
    }

    [Theory]
    [InlineData(EditionStatus.InProgress)]
    [InlineData(EditionStatus.Closed)]
    public async Task Deleting_a_started_edition_is_blocking(EditionStatus status)
    {
        var (id, _) = await _admin.CreateCompleteEditionAsync(2031);
        await _host.SetStateAsync(id, status);

        using var response = await _admin.DeleteAsync($"/api/editions/{id}", TestContext.Current.CancellationToken);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "editions.notDraft");
        Assert.Equal(2031, (await _admin.GetEditionAsync(id)).Year);
    }

    [Fact]
    public async Task Writing_an_unknown_edition_is_not_found()
    {
        var unknown = Guid.CreateVersion7();

        using var edit = await _admin.PutAsJsonAsync($"/api/editions/{unknown}", CompleteBody(2031, 1), TestContext.Current.CancellationToken);
        using var delete = await _admin.DeleteAsync($"/api/editions/{unknown}", TestContext.Current.CancellationToken);

        await AssertProblemAsync(edit, HttpStatusCode.NotFound, "editions.notFound");
        await AssertProblemAsync(delete, HttpStatusCode.NotFound, "editions.notFound");
    }

    [Fact]
    public async Task A_firing_chief_cannot_manage_editions()
    {
        var (id, version) = await _admin.CreateEditionAsync(2031);
        await _host.SetStateAsync(id, EditionStatus.InProgress);

        using var create = await _chief.PostAsync("/api/editions", new { year = 2032, festivalStartsOn = "2032-04-22", festivalEndsOn = "2032-04-25" });
        using var edit = await _chief.PutAsJsonAsync($"/api/editions/{id}", CompleteBody(2031, version), TestContext.Current.CancellationToken);
        using var delete = await _chief.DeleteAsync($"/api/editions/{id}", TestContext.Current.CancellationToken);

        Assert.All([create, edit, delete], r => Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode));
        Assert.Single(await _host.AuditEntriesAsync("EditionCreated"));
    }

    [Fact]
    public async Task A_firing_chief_lists_editions_without_drafts_newest_first()
    {
        var (closed, _) = await _admin.CreateCompleteEditionAsync(2030);
        var (current, _) = await _admin.CreateCompleteEditionAsync(2031);
        await _admin.CreateEditionAsync(2032);
        await _host.SetStateAsync(closed, EditionStatus.Closed);
        await _host.SetStateAsync(current, EditionStatus.InProgress, ordersOpen: true);

        using var chief = await _chief.GetAsync("/api/editions", TestContext.Current.CancellationToken);
        using var admin = await _admin.GetAsync("/api/editions", TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                new EditionRowJson(current, 2031, new DateOnly(2031, 4, 22), new DateOnly(2031, 4, 25), "IN_PROGRESS", true, true),
                new EditionRowJson(closed, 2030, new DateOnly(2030, 4, 22), new DateOnly(2030, 4, 25), "CLOSED", false, false),
            ],
            await ReadAsync<List<EditionRowJson>>(chief));
        Assert.Equal([2032, 2031, 2030], (await ReadAsync<List<EditionRowJson>>(admin)).Select(e => e.Year));
    }

    [Fact]
    public async Task A_firing_chief_reads_a_started_edition_but_not_a_draft()
    {
        var (draft, _) = await _admin.CreateEditionAsync(2032);
        var (closed, _) = await _admin.CreateCompleteEditionAsync(2030);
        await _host.SetStateAsync(closed, EditionStatus.Closed);

        using var hidden = await _chief.GetAsync($"/api/editions/{draft}", TestContext.Current.CancellationToken);

        await AssertProblemAsync(hidden, HttpStatusCode.NotFound, "editions.notFound");
        Assert.Equal(55.00m, (await _chief.GetEditionAsync(closed)).Prices.PowderPerKg);
    }

    [Fact]
    public async Task Without_an_edition_in_progress_there_is_no_current_edition()
    {
        var (closed, _) = await _admin.CreateCompleteEditionAsync(2030);
        await _host.SetStateAsync(closed, EditionStatus.Closed);
        await _admin.CreateEditionAsync(2031);

        using var response = await _chief.GetAsync("/api/editions/current", TestContext.Current.CancellationToken);

        Assert.Null((await ReadAsync<CurrentJson>(response)).Edition);
    }
}
