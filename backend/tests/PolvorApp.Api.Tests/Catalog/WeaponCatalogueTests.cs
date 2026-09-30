using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.FederationCatalog.Endpoints;
using PolvorApp.IdentityAccess.Contracts;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Catalog;

/// <summary>
/// Specs "Weapon catalogue access" (reads for everyone, writes for Admins) and the model part of
/// "Deleting comparsas and weapon models".
/// </summary>
[Collection(PostgresGroup.Name)]
public sealed class WeaponCatalogueTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private readonly FakeCatalogUsage _usage = new();
    private IdentityTestHost _host = null!;
    private SyntheticUser _adminUser = null!;
    private HttpClient _admin = null!;

    public async ValueTask InitializeAsync()
    {
        _host = await IdentityTestHost.StartAsync(postgres, mailpit, configureServices: services => services.AddSingleton<ICatalogUsage>(_usage));
        _adminUser = await _host.CreateUserAsync("admin.catalogo@example.test", UserRole.Admin);
        _admin = await _host.SignInAsync(_adminUser);
    }

    public async ValueTask DisposeAsync()
    {
        _admin.Dispose();
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task A_firing_chief_reads_the_active_catalogue_sorted_by_label()
    {
        await CreateAsync(Arcabuz("ARCABUZ MORO ZURDO", "LEFT"));
        await CreateAsync(new { kind = "PISTOL", label = "PISTOLA", rentable = false });
        await CreateAsync(Arcabuz("ARCABUZ MORO DIESTRO", "RIGHT"));
        var retired = await CreateAsync(Arcabuz("ARCABUZ RETIRADO", "RIGHT", "SMALL"));
        (await _admin.PostAsync($"/api/weapon-models/{retired.Id}/deactivate", new { })).Dispose();
        using var chief = await _host.SignInAsync(await _host.CreateUserAsync("jefe.catalogo@example.test"));

        var labels = (await ListAsync(chief, string.Empty)).Select(m => m.Label);

        Assert.Equal(["ARCABUZ MORO DIESTRO", "ARCABUZ MORO ZURDO", "PISTOLA"], labels);
    }

    [Fact]
    public async Task The_catalogue_filters_by_kind_and_can_include_inactive_models()
    {
        await CreateAsync(new { kind = "PISTOL", label = "PISTOLA", rentable = false });
        var retired = await CreateAsync(Arcabuz("ARCABUZ RETIRADO", "RIGHT"));
        (await _admin.PostAsync($"/api/weapon-models/{retired.Id}/deactivate", new { })).Dispose();

        Assert.Empty(await ListAsync(_admin, "?kind=ARCABUZ"));
        var arcabuz = Assert.Single(await ListAsync(_admin, "?kind=ARCABUZ&includeInactive=true"));
        Assert.False(arcabuz.Active);
        Assert.Equal(2, (await ListAsync(_admin, "?includeInactive=true")).Count);
    }

    [Fact]
    public async Task An_invalid_kind_filter_is_named()
    {
        using var response = await _admin.GetAsync("/api/weapon-models?kind=CANNON", TestContext.Current.CancellationToken);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation");
        Assert.Equal("invalid", (await ErrorsAsync(response))["kind"]);
    }

    [Fact]
    public async Task One_model_is_read_by_id_and_an_unknown_one_is_not_found()
    {
        var model = await CreateAsync(new { kind = "PISTOL", label = "PISTOLA", rentable = false });
        using var chief = await _host.SignInAsync(await _host.CreateUserAsync("jefe.lector@example.test"));

        using var found = await chief.GetAsync($"/api/weapon-models/{model.Id}", TestContext.Current.CancellationToken);
        using var missing = await chief.GetAsync($"/api/weapon-models/{Guid.CreateVersion7()}", TestContext.Current.CancellationToken);

        Assert.Equal(model, await ReadAsync<WeaponModelResponse>(found));
        await AssertProblemAsync(missing, HttpStatusCode.NotFound, "weaponModels.notFound");
    }

    [Fact]
    public async Task Deactivation_and_reactivation_are_audited_once_each()
    {
        var model = await CreateAsync(Arcabuz("ARCABUZ DURMIENTE", "RIGHT"));

        using var off = await _admin.PostAsync($"/api/weapon-models/{model.Id}/deactivate", new { });
        using var offAgain = await _admin.PostAsync($"/api/weapon-models/{model.Id}/deactivate", new { });
        using var on = await _admin.PostAsync($"/api/weapon-models/{model.Id}/reactivate", new { });
        using var onAgain = await _admin.PostAsync($"/api/weapon-models/{model.Id}/reactivate", new { });

        Assert.False((await ReadAsync<WeaponModelResponse>(offAgain)).Active);
        Assert.True((await ReadAsync<WeaponModelResponse>(onAgain)).Active);
        Assert.Single(await _host.AuditEntriesAsync("WeaponModelDeactivated"));
        Assert.Single(await _host.AuditEntriesAsync("WeaponModelReactivated"));
    }

    [Theory]
    [InlineData("deactivate")]
    [InlineData("reactivate")]
    public async Task Changing_the_state_of_an_unknown_model_is_not_found(string action)
    {
        using var response = await _admin.PostAsync($"/api/weapon-models/{Guid.CreateVersion7()}/{action}", new { });

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "weaponModels.notFound");
    }

    [Fact]
    public async Task An_unused_model_is_deleted_audited_with_a_snapshot_and_its_label_can_be_reused()
    {
        var model = await CreateAsync(Arcabuz("ARCABUZ EFÍMERO", "LEFT"));

        using var deleted = await _admin.DeleteAsync($"/api/weapon-models/{model.Id}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        using var gone = await _admin.GetAsync($"/api/weapon-models/{model.Id}", TestContext.Current.CancellationToken);
        await AssertProblemAsync(gone, HttpStatusCode.NotFound, "weaponModels.notFound");
        var entry = Assert.Single(await _host.AuditEntriesAsync("WeaponModelDeleted"));
        Assert.Equal((_adminUser.Id, model.Id.ToString()), (entry.ActorUserId, entry.EntityId));
        using var snapshot = JsonDocument.Parse(entry.Data!);
        Assert.Equal("ARCABUZ EFÍMERO", snapshot.RootElement.GetProperty("label").GetString());
        Assert.NotEqual(model.Id, (await CreateAsync(Arcabuz("arcabuz efímero", "LEFT"))).Id);
    }

    [Fact]
    public async Task A_model_in_use_is_not_deleted()
    {
        var model = await CreateAsync(Arcabuz("ARCABUZ OCUPADO", "LEFT"));
        _usage.WeaponModelsInUse.Add(model.Id);

        using var response = await _admin.DeleteAsync($"/api/weapon-models/{model.Id}", TestContext.Current.CancellationToken);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "weaponModels.inUse");
        using var still = await _admin.GetAsync($"/api/weapon-models/{model.Id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, still.StatusCode);
        Assert.Empty(await _host.AuditEntriesAsync("WeaponModelDeleted"));
    }

    [Fact]
    public async Task Deleting_an_unknown_model_is_not_found()
    {
        using var response = await _admin.DeleteAsync($"/api/weapon-models/{Guid.CreateVersion7()}", TestContext.Current.CancellationToken);

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "weaponModels.notFound");
    }

    [Fact]
    public async Task A_firing_chief_cannot_change_the_state_of_or_delete_a_model()
    {
        var model = await CreateAsync(Arcabuz("ARCABUZ BLINDADO", "LEFT"));
        using var chief = await _host.SignInAsync(await _host.CreateUserAsync("jefe.estado.modelo@example.test"));

        using var off = await chief.PostAsync($"/api/weapon-models/{model.Id}/deactivate", new { });
        using var on = await chief.PostAsync($"/api/weapon-models/{model.Id}/reactivate", new { });
        using var delete = await chief.DeleteAsync($"/api/weapon-models/{model.Id}", TestContext.Current.CancellationToken);

        Assert.All([off, on, delete], r => Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode));
        using var unchanged = await _admin.GetAsync($"/api/weapon-models/{model.Id}", TestContext.Current.CancellationToken);
        Assert.True((await ReadAsync<WeaponModelResponse>(unchanged)).Active);
        Assert.Empty(await _host.AuditEntriesAsync("WeaponModelDeactivated"));
        Assert.Empty(await _host.AuditEntriesAsync("WeaponModelDeleted"));
    }

    [Fact]
    public async Task The_catalogue_is_not_readable_without_signing_in()
    {
        using var anonymous = await _host.NewClientAsync();

        using var response = await anonymous.GetAsync("/api/weapon-models", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static object Arcabuz(string label, string handedness, string size = "NORMAL") =>
        new { kind = "ARCABUZ", side = "MOORISH", handedness, size, rentable = true, label };

    private static async Task<List<WeaponModelResponse>> ListAsync(HttpClient client, string query)
    {
        using var response = await client.GetAsync($"/api/weapon-models{query}", TestContext.Current.CancellationToken);
        return await ReadAsync<List<WeaponModelResponse>>(response);
    }

    private async Task<WeaponModelResponse> CreateAsync(object body)
    {
        using var response = await _admin.PostAsync("/api/weapon-models", body);
        return await ReadAsync<WeaponModelResponse>(response);
    }
}
