using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.FederationCatalog.Endpoints;
using PolvorApp.IdentityAccess.Contracts;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Catalog;

/// <summary>Specs "Comparsas", "Comparsa management by Admins" and "Catalogue changes are audited".</summary>
public sealed class ComparsaManagementTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private IdentityTestHost _host = null!;
    private SyntheticUser _adminUser = null!;
    private HttpClient _admin = null!;

    public async ValueTask InitializeAsync()
    {
        _host = await IdentityTestHost.StartAsync(postgres, mailpit);
        _adminUser = await _host.CreateUserAsync("admin.comparsas@example.test", UserRole.Admin);
        _admin = await _host.SignInAsync(_adminUser);
    }

    public async ValueTask DisposeAsync()
    {
        _admin.Dispose();
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task An_admin_creates_and_edits_a_comparsa_and_both_changes_are_audited()
    {
        using var created = await _admin.PostAsync("/api/comparsas", new { name = "  Comparsa Sintética Norte  ", side = "CHRISTIAN" });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var comparsa = await ReadAsync<ComparsaResponse>(created);
        Assert.Equal(("Comparsa Sintética Norte", Side.Christian, true), (comparsa.Name, comparsa.Side, comparsa.Active));
        Assert.Equal($"/api/comparsas/{comparsa.Id}", created.Headers.Location?.OriginalString);

        using var edited = await _admin.PutAsJsonAsync(
            $"/api/comparsas/{comparsa.Id}", new { name = "Comparsa Sintética Nord", side = "MOORISH" }, TestContext.Current.CancellationToken);

        var renamed = await ReadAsync<ComparsaResponse>(edited);
        Assert.Equal(("Comparsa Sintética Nord", Side.Moorish), (renamed.Name, renamed.Side));
        var creation = Assert.Single(await _host.AuditEntriesAsync("ComparsaCreated"));
        Assert.Equal((_adminUser.Id, comparsa.Id, comparsa.Id.ToString()), (creation.ActorUserId, creation.ComparsaId, creation.EntityId));
        Assert.Contains("\"CHRISTIAN\"", creation.Data, StringComparison.Ordinal);
        var update = Assert.Single(await _host.AuditEntriesAsync("ComparsaUpdated"));
        Assert.Equal((_adminUser.Id, comparsa.Id), (update.ActorUserId, update.ComparsaId));
        using var data = JsonDocument.Parse(update.Data!);
        Assert.Equal(("Comparsa Sintética Norte", "CHRISTIAN"), NameAndSide(data.RootElement.GetProperty("previous")));
        Assert.Equal(("Comparsa Sintética Nord", "MOORISH"), NameAndSide(data.RootElement.GetProperty("current")));
    }

    [Theory]
    [InlineData("comparsa sintética NORTE")]
    [InlineData("COMPARSA SINTÉTICA NORTE")]
    [InlineData("Comparsa Sinte\u0301tica Norte")]
    public async Task A_duplicate_name_is_blocking_ignoring_case_and_unicode_form(string duplicate)
    {
        var existing = await CreateAsync("Comparsa Sintética Norte");
        var other = await CreateAsync("Comparsa Sintética Sur");

        using var create = await _admin.PostAsync("/api/comparsas", new { name = duplicate, side = "MOORISH" });
        using var rename = await _admin.PutAsJsonAsync($"/api/comparsas/{other.Id}", new { name = duplicate, side = "MOORISH" }, TestContext.Current.CancellationToken);

        await AssertProblemAsync(create, HttpStatusCode.Conflict, "comparsas.nameTaken");
        await AssertProblemAsync(rename, HttpStatusCode.Conflict, "comparsas.nameTaken");
        Assert.Single(await _host.AuditEntriesAsync("ComparsaCreated"), e => e.ComparsaId == existing.Id);
        Assert.Empty(await _host.AuditEntriesAsync("ComparsaUpdated"));
    }

    [Fact]
    public async Task Concurrent_creations_of_the_same_name_store_one_comparsa()
    {
        var attempts = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            _admin.PostAsJsonAsync("/api/comparsas", new { name = "Comparsa Sintética Carrera", side = "MOORISH" }, TestContext.Current.CancellationToken)));

        Assert.Single(attempts, r => r.StatusCode == HttpStatusCode.Created);
        foreach (var conflict in attempts.Where(r => r.StatusCode != HttpStatusCode.Created))
        {
            await AssertProblemAsync(conflict, HttpStatusCode.Conflict, "comparsas.nameTaken");
        }

        Assert.Single(await _host.AuditEntriesAsync("ComparsaCreated"));
    }

    [Theory]
    [InlineData("", "CHRISTIAN", "name", "required")]
    [InlineData("   ", "CHRISTIAN", "name", "required")]
    [InlineData(null, "CHRISTIAN", "name", "required")]
    [InlineData("Comparsa Sintética", "NEUTRAL", "side", "invalid")]
    [InlineData("Comparsa Sintética", "christian", "side", "invalid")]
    [InlineData("Comparsa Sintética", null, "side", "required")]
    [InlineData("Comparsa Sintética", "", "side", "required")]
    [InlineData("Comparsa\u0000Nula", "CHRISTIAN", "name", "invalid")]
    [InlineData("\u200B", "CHRISTIAN", "name", "invalid")]
    [InlineData("Comparsa\u200BInvisible", "CHRISTIAN", "name", "invalid")]
    [InlineData("\u202EComparsa Invertida", "CHRISTIAN", "name", "invalid")]
    [InlineData("Comparsa\nPartida", "CHRISTIAN", "name", "invalid")]
    public async Task Invalid_fields_are_named(string? name, string? side, string field, string reason)
    {
        using var response = await _admin.PostAsync("/api/comparsas", new { name, side });

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation");
        Assert.Equal(reason, (await ErrorsAsync(response))[field]);
    }

    [Fact]
    public async Task An_edit_names_every_invalid_field()
    {
        var comparsa = await CreateAsync("Comparsa Sintética Editada");

        using var response = await _admin.PutAsJsonAsync($"/api/comparsas/{comparsa.Id}", new { name = " ", side = "NEUTRAL" }, TestContext.Current.CancellationToken);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation");
        Assert.Equal(new Dictionary<string, string> { ["name"] = "required", ["side"] = "invalid" }, await ErrorsAsync(response));
    }

    [Fact]
    public async Task A_name_of_exactly_100_characters_is_accepted()
    {
        var name = new string('x', 100);

        Assert.Equal(name, (await CreateAsync(name)).Name);
    }

    [Fact]
    public async Task A_comparsa_can_change_the_case_of_its_own_name()
    {
        var comparsa = await CreateAsync("comparsa sintética minúscula");

        using var response = await _admin.PutAsJsonAsync(
            $"/api/comparsas/{comparsa.Id}", new { name = "Comparsa Sintética Minúscula", side = "CHRISTIAN" }, TestContext.Current.CancellationToken);

        Assert.Equal("Comparsa Sintética Minúscula", (await ReadAsync<ComparsaResponse>(response)).Name);
        Assert.Single(await _host.AuditEntriesAsync("ComparsaUpdated"));
    }

    [Fact]
    public async Task Writes_need_a_signed_in_user()
    {
        var comparsa = await CreateAsync("Comparsa Sintética Anónima");
        using var anonymous = await _host.NewClientAsync();

        using var create = await anonymous.PostAsync("/api/comparsas", new { name = "Comparsa Anónima", side = "MOORISH" });
        using var delete = await anonymous.DeleteAsync($"/api/comparsas/{comparsa.Id}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, create.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, delete.StatusCode);
    }

    [Fact]
    public async Task A_name_longer_than_100_characters_is_rejected()
    {
        using var response = await _admin.PostAsync("/api/comparsas", new { name = new string('x', 101), side = "MOORISH" });

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation");
        Assert.Equal("tooLong", (await ErrorsAsync(response))["name"]);
    }

    [Fact]
    public async Task An_unchanged_edit_is_not_audited()
    {
        var comparsa = await CreateAsync("Comparsa Sintética Quieta");

        using var response = await _admin.PutAsJsonAsync(
            $"/api/comparsas/{comparsa.Id}", new { name = " Comparsa Sintética Quieta ", side = "CHRISTIAN" }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await _host.AuditEntriesAsync("ComparsaUpdated"));
    }

    [Fact]
    public async Task Editing_an_unknown_comparsa_is_not_found()
    {
        using var response = await _admin.PutAsJsonAsync(
            $"/api/comparsas/{Guid.CreateVersion7()}", new { name = "Nadie", side = "CHRISTIAN" }, TestContext.Current.CancellationToken);

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "comparsas.notFound");
    }

    [Fact]
    public async Task A_firing_chief_cannot_create_or_edit_comparsas()
    {
        var comparsa = await CreateAsync("Comparsa Sintética Ajena");
        using var chief = await _host.SignInAsync(await _host.CreateUserAsync("jefe.comparsas@example.test"));

        using var create = await chief.PostAsync("/api/comparsas", new { name = "Comparsa Sintética Intrusa", side = "MOORISH" });
        using var edit = await chief.PutAsJsonAsync($"/api/comparsas/{comparsa.Id}", new { name = "Pirata", side = "MOORISH" }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, edit.StatusCode);
        Assert.Single(await _host.AuditEntriesAsync("ComparsaCreated"));
        Assert.Equal(("Comparsa Sintética Ajena", Side.Christian), await NameAndSideAsync(comparsa.Id));
    }

    [Fact]
    public async Task Deactivation_and_reactivation_change_the_state_keep_assignments_and_are_audited_once()
    {
        var comparsa = await CreateAsync("Comparsa Sintética Durmiente");
        var chiefId = Guid.CreateVersion7();
        await _host.AssignAsync(comparsa.Id, chiefId);

        using var deactivated = await _admin.PostAsync($"/api/comparsas/{comparsa.Id}/deactivate", new { });
        using var again = await _admin.PostAsync($"/api/comparsas/{comparsa.Id}/deactivate", new { });

        Assert.False((await ReadAsync<ComparsaResponse>(deactivated)).Active);
        Assert.False((await ReadAsync<ComparsaResponse>(again)).Active);
        Assert.Equal([comparsa.Id], await _host.AssignedComparsasAsync(chiefId));
        var deactivation = Assert.Single(await _host.AuditEntriesAsync("ComparsaDeactivated"));
        Assert.Equal((_adminUser.Id, comparsa.Id), (deactivation.ActorUserId, deactivation.ComparsaId));

        using var reactivated = await _admin.PostAsync($"/api/comparsas/{comparsa.Id}/reactivate", new { });
        using var twice = await _admin.PostAsync($"/api/comparsas/{comparsa.Id}/reactivate", new { });

        Assert.True((await ReadAsync<ComparsaResponse>(reactivated)).Active);
        Assert.True((await ReadAsync<ComparsaResponse>(twice)).Active);
        var reactivation = Assert.Single(await _host.AuditEntriesAsync("ComparsaReactivated"));
        Assert.Equal((_adminUser.Id, comparsa.Id), (reactivation.ActorUserId, reactivation.ComparsaId));
    }

    private static (string? Name, string? Side) NameAndSide(JsonElement snapshot) =>
        (snapshot.GetProperty("name").GetString(), snapshot.GetProperty("side").GetString());

    [Theory]
    [InlineData("deactivate")]
    [InlineData("reactivate")]
    public async Task Changing_the_state_of_an_unknown_comparsa_is_not_found(string action)
    {
        using var response = await _admin.PostAsync($"/api/comparsas/{Guid.CreateVersion7()}/{action}", new { });

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "comparsas.notFound");
    }

    [Theory]
    [InlineData("deactivate")]
    [InlineData("reactivate")]
    public async Task A_firing_chief_cannot_change_the_state_of_a_comparsa(string action)
    {
        var comparsa = await CreateAsync("Comparsa Sintética Protegida");
        using var chief = await _host.SignInAsync(await _host.CreateUserAsync("jefe.estado@example.test"));

        using var response = await chief.PostAsync($"/api/comparsas/{comparsa.Id}/{action}", new { });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(await _host.AuditEntriesAsync("ComparsaDeactivated"));
        using var unchanged = await _admin.GetAsync($"/api/comparsas/{comparsa.Id}", TestContext.Current.CancellationToken);
        Assert.True((await ReadAsync<ComparsaResponse>(unchanged)).Active);
    }

    private async Task<(string Name, Side Side)> NameAndSideAsync(Guid id)
    {
        using var response = await _admin.GetAsync($"/api/comparsas/{id}", TestContext.Current.CancellationToken);
        var comparsa = await ReadAsync<ComparsaResponse>(response);
        return (comparsa.Name, comparsa.Side);
    }

    private async Task<ComparsaResponse> CreateAsync(string name, string side = "CHRISTIAN")
    {
        using var response = await _admin.PostAsync("/api/comparsas", new { name, side });
        return await ReadAsync<ComparsaResponse>(response);
    }
}
