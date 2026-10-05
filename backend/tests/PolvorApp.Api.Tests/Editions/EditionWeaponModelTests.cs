using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.IdentityAccess.Contracts;
using static PolvorApp.Api.Tests.Infrastructure.EditionData;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Editions;

/// <summary>Spec "Rental models offered in an edition" and its audit.</summary>
public sealed class EditionWeaponModelTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private IdentityTestHost _host = null!;
    private HttpClient _admin = null!;

    public async ValueTask InitializeAsync()
    {
        _host = await IdentityTestHost.StartAsync(postgres, mailpit);
        _admin = await _host.SignInAsync(await _host.CreateUserAsync("admin.modelos.edicion@example.test", UserRole.Admin));
    }

    public async ValueTask DisposeAsync()
    {
        _admin.Dispose();
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task An_admin_replaces_the_set_and_the_changes_are_audited()
    {
        var (id, _) = await _admin.CreateEditionAsync(2031);
        var first = await _admin.CreateRentableModelAsync("ARCABUZ MORO DIESTRO");
        var second = await _admin.CreateRentableModelAsync("ARCABUZ MORO ZURDO", handedness: "LEFT");
        var third = await _admin.CreateRentableModelAsync("ARCABUZ MORO PEQUEÑO", size: "SMALL");
        await SetAsync(id, first, second);

        using var response = await SetAsync(id, second, third, third);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var edition = await ReadAsync<EditionJson>(response);
        Assert.Equal(["ARCABUZ MORO PEQUEÑO", "ARCABUZ MORO ZURDO"], edition.WeaponModels.Select(m => m.Label));
        Assert.All(edition.WeaponModels, m => Assert.True(m.Offered));
        var entries = await _host.AuditEntriesAsync("EditionWeaponModelsChanged");
        Assert.Equal(2, entries.Count);
        // The test clock stands still, so the second save is the entry that adds the third model.
        using var data = JsonDocument.Parse(entries.Single(e => e.Data!.Contains(third.ToString(), StringComparison.Ordinal)).Data!);
        Assert.Equal([third], data.RootElement.GetProperty("added").EnumerateArray().Select(e => e.GetGuid()));
        Assert.Equal([first], data.RootElement.GetProperty("removed").EnumerateArray().Select(e => e.GetGuid()));
    }

    [Fact]
    public async Task Saving_the_same_set_is_not_audited()
    {
        var (id, _) = await _admin.CreateEditionAsync(2031);
        var model = await _admin.CreateRentableModelAsync("ARCABUZ REPETIDO");
        using (await SetAsync(id, model))
        {
        }

        using var response = await SetAsync(id, model);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(await _host.AuditEntriesAsync("EditionWeaponModelsChanged"));
    }

    [Fact]
    public async Task A_rentable_pistol_can_be_offered()
    {
        var (id, _) = await _admin.CreateEditionAsync(2031);
        using var created = await _admin.PostAsync("/api/weapon-models", new { kind = "PISTOL", label = "PISTOLA", rentable = true });
        var pistol = (await ReadAsync<IdJson>(created)).Id;

        using var response = await SetAsync(id, pistol);

        var model = Assert.Single((await ReadAsync<EditionJson>(response)).WeaponModels);
        Assert.Equal(("PISTOLA", true), (model.Label, model.Offered));
    }

    [Fact]
    public async Task A_non_rentable_pistol_cannot_be_offered()
    {
        var (id, _) = await _admin.CreateEditionAsync(2031);
        using var created = await _admin.PostAsync("/api/weapon-models", new { kind = "PISTOL", label = "PISTOLA", rentable = false });
        var pistol = (await ReadAsync<IdJson>(created)).Id;

        using var response = await SetAsync(id, pistol);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation");
        Assert.Equal(new Dictionary<string, string> { ["weaponModelIds"] = "notRentable" }, await ErrorsAsync(response));
        Assert.Empty((await _admin.GetEditionAsync(id)).WeaponModels);
    }

    [Fact]
    public async Task An_inactive_model_cannot_be_added()
    {
        var (id, _) = await _admin.CreateEditionAsync(2031);
        var model = await _admin.CreateRentableModelAsync("ARCABUZ INACTIVO");
        using (var deactivate = await _admin.PostAsync($"/api/weapon-models/{model}/deactivate", new { }))
        {
            deactivate.EnsureSuccessStatusCode();
        }

        using var response = await SetAsync(id, model);

        Assert.Equal("notRentable", (await ErrorsAsync(response))["weaponModelIds"]);
    }

    [Fact]
    public async Task An_unknown_model_is_not_found()
    {
        var (id, _) = await _admin.CreateEditionAsync(2031);

        using var response = await SetAsync(id, Guid.CreateVersion7());

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation");
        Assert.Equal("notFound", (await ErrorsAsync(response))["weaponModelIds"]);
    }

    [Fact]
    public async Task A_model_deactivated_after_being_offered_is_kept_marked_and_can_be_removed()
    {
        var (id, _) = await _admin.CreateEditionAsync(2031);
        var retired = await _admin.CreateRentableModelAsync("ARCABUZ RETIRADO");
        var other = await _admin.CreateRentableModelAsync("ARCABUZ VIGENTE", handedness: "LEFT");
        using (await SetAsync(id, retired, other))
        {
        }

        using (var deactivate = await _admin.PostAsync($"/api/weapon-models/{retired}/deactivate", new { }))
        {
            deactivate.EnsureSuccessStatusCode();
        }

        var marked = await _admin.GetEditionAsync(id);
        Assert.False(marked.WeaponModels.Single(m => m.Id == retired).Offered);

        using var kept = await SetAsync(id, retired, other);
        Assert.Equal(HttpStatusCode.OK, kept.StatusCode);

        using var removed = await SetAsync(id, other);
        Assert.Equal([other], (await ReadAsync<EditionJson>(removed)).WeaponModels.Select(m => m.Id));
    }

    [Fact]
    public async Task The_set_is_required_and_limited()
    {
        var (id, _) = await _admin.CreateEditionAsync(2031);

        using var missing = await _admin.PutAsJsonAsync($"/api/editions/{id}/weapon-models", new { }, TestContext.Current.CancellationToken);
        using var tooMany = await _admin.PutAsJsonAsync(
            $"/api/editions/{id}/weapon-models",
            new { weaponModelIds = Enumerable.Range(0, 101).Select(_ => Guid.CreateVersion7()) },
            TestContext.Current.CancellationToken);

        Assert.Equal("required", (await ErrorsAsync(missing))["weaponModelIds"]);
        Assert.Equal("tooMany", (await ErrorsAsync(tooMany))["weaponModelIds"]);
    }

    [Fact]
    public async Task An_unknown_edition_is_not_found()
    {
        using var response = await SetAsync(Guid.CreateVersion7());

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "editions.notFound");
    }

    [Fact]
    public async Task A_firing_chief_cannot_change_the_models()
    {
        var (id, _) = await _admin.CreateEditionAsync(2031);
        using var chief = await _host.SignInAsync(await _host.CreateUserAsync("jefe.modelos.edicion@example.test"));

        using var response = await chief.PutAsJsonAsync($"/api/editions/{id}/weapon-models", new { weaponModelIds = Array.Empty<Guid>() }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private Task<HttpResponseMessage> SetAsync(Guid editionId, params Guid[] modelIds) =>
        _admin.PutAsJsonAsync($"/api/editions/{editionId}/weapon-models", new { weaponModelIds = modelIds }, TestContext.Current.CancellationToken);
}
