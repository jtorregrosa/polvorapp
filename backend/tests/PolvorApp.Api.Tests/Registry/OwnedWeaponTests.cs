using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.FederationCatalog.WeaponModels;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Registry;

/// <summary>Spec "Owned weapons (UC-04)" and "Registry changes are audited" (owned weapons).</summary>
[Collection(PostgresGroup.Name)]
public sealed class OwnedWeaponTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private readonly WeaponModel _arcabuz = RegistryData.NewWeaponModel("ARCABUZ MORO DIESTRO");
    private readonly WeaponModel _pistol = RegistryData.NewWeaponModel("PISTOLA", WeaponKind.Pistol);
    private readonly WeaponModel _retired = RegistryData.NewWeaponModel("TRABUCO RETIRADO", WeaponKind.Trabuco, active: false);
    private RegistryTestHost _registry = null!;
    private JsonElement _owner;

    public async ValueTask InitializeAsync()
    {
        _registry = await RegistryTestHost.StartAsync(postgres, mailpit);
        await _registry.Services.SaveCatalogAsync(_arcabuz, _pistol, _retired);
        _owner = await _registry.RegisterAsync(_registry.Own.Id);
    }

    public async ValueTask DisposeAsync() => await _registry.DisposeAsync();

    [Fact]
    public async Task A_FiringChief_adds_an_owned_weapon_with_an_upper_cased_guide()
    {
        using var response = await AddAsync(_registry.FiringChief, Id(_owner), _arcabuz.Id, " ab-123-45 ");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var weapon = await ReadAsync<JsonElement>(response);
        Assert.Equal("AB-123-45", weapon.GetProperty("ownershipGuideNumber").GetString());
        Assert.Equal("ARCABUZ MORO DIESTRO", weapon.GetProperty("model").GetProperty("label").GetString());
        var detail = await DetailAsync(Id(_owner));
        Assert.Equal(weapon.GetProperty("id").GetString(), Assert.Single(detail.GetProperty("ownedWeapons").EnumerateArray()).GetProperty("id").GetString());
    }

    [Fact]
    public async Task A_pistol_can_be_owned()
    {
        using var response = await AddAsync(_registry.FiringChief, Id(_owner), _pistol.Id, "SINT-1001");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task An_unknown_model_is_invalid_and_an_inactive_one_is_blocking()
    {
        using var unknown = await AddAsync(_registry.FiringChief, Id(_owner), Guid.CreateVersion7(), "SINT-1002");
        using var inactive = await AddAsync(_registry.FiringChief, Id(_owner), _retired.Id, "SINT-1003");

        await AssertProblemAsync(unknown, HttpStatusCode.BadRequest, "validation");
        Assert.Equal("notFound", (await ErrorsAsync(unknown))["weaponModelId"]);
        await AssertProblemAsync(inactive, HttpStatusCode.Conflict, "ownedWeapons.modelInactive");
    }

    [Fact]
    public async Task An_existing_weapon_keeps_a_model_deactivated_later_but_cannot_move_to_one()
    {
        var weapon = await ReadAsync<JsonElement>(await AddAsync(_registry.FiringChief, Id(_owner), _arcabuz.Id, "SINT-1004"));
        await DeactivateAsync(_arcabuz.Id);

        using var renumbered = await EditAsync(_registry.FiringChief, Id(_owner), weapon, _arcabuz.Id, "SINT-1004", weaponNumber: "4321");
        var current = await ReadAsync<JsonElement>(renumbered);
        using var moved = await EditAsync(_registry.FiringChief, Id(_owner), current, _retired.Id, "SINT-1004");

        Assert.Equal("4321", current.GetProperty("weaponNumber").GetString());
        Assert.False(current.GetProperty("model").GetProperty("active").GetBoolean());
        await AssertProblemAsync(moved, HttpStatusCode.Conflict, "ownedWeapons.modelInactive");
    }

    [Fact]
    public async Task A_guide_already_used_in_any_comparsa_is_blocking_and_reveals_nothing()
    {
        var other = await _registry.RegisterAsync(_registry.Other.Id, body => body["lastName"] = "Dueño Oculto");
        using var _ = await AddAsync(_registry.Admin, Id(other), _arcabuz.Id, "AB-123-45");

        using var response = await AddAsync(_registry.FiringChief, Id(_owner), _arcabuz.Id, "ab-123-45");

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "ownedWeapons.guideTaken");
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain(Id(other), text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Oculto", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_same_weapon_number_may_appear_twice()
    {
        using var first = await AddAsync(_registry.FiringChief, Id(_owner), _arcabuz.Id, "SINT-1005", weaponNumber: "1234");
        using var second = await AddAsync(_registry.FiringChief, Id(_owner), _pistol.Id, "SINT-1006", weaponNumber: "1234");

        Assert.Equal((HttpStatusCode.Created, HttpStatusCode.Created), (first.StatusCode, second.StatusCode));
    }

    [Fact]
    public async Task An_outdated_weapon_version_is_rejected()
    {
        var weapon = await ReadAsync<JsonElement>(await AddAsync(_registry.FiringChief, Id(_owner), _arcabuz.Id, "SINT-1007"));
        using var first = await EditAsync(_registry.FiringChief, Id(_owner), weapon, _arcabuz.Id, "SINT-1007", weaponNumber: "1");
        using var stale = await EditAsync(_registry.Admin, Id(_owner), weapon, _arcabuz.Id, "SINT-1007", weaponNumber: "2");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        await AssertProblemAsync(stale, HttpStatusCode.Conflict, "ownedWeapons.modified");
    }

    [Fact]
    public async Task Invalid_weapon_fields_are_named()
    {
        using var response = await _registry.FiringChief.PostAsJsonAsync(
            $"/api/arquebusiers/{Id(_owner)}/owned-weapons",
            new { weaponNumber = " ", ownershipGuideNumber = new string('A', 31) },
            TestContext.Current.CancellationToken);

        var errors = await ErrorsAsync(response);
        Assert.Equal(("required", "required", "tooLong"), (errors["weaponModelId"], errors["weaponNumber"], errors["ownershipGuideNumber"]));
    }

    [Fact]
    public async Task An_edit_to_a_guide_already_used_elsewhere_is_blocking()
    {
        using var _ = await AddAsync(_registry.FiringChief, Id(_owner), _arcabuz.Id, "SINT-1020");
        var weapon = await ReadAsync<JsonElement>(await AddAsync(_registry.FiringChief, Id(_owner), _pistol.Id, "SINT-1021"));

        using var response = await EditAsync(_registry.FiringChief, Id(_owner), weapon, _pistol.Id, "sint-1020");

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "ownedWeapons.guideTaken");
    }

    [Fact]
    public async Task An_edit_needs_the_version_and_unknown_members_are_rejected()
    {
        var weapon = await ReadAsync<JsonElement>(await AddAsync(_registry.FiringChief, Id(_owner), _arcabuz.Id, "SINT-1022"));
        var url = $"/api/arquebusiers/{Id(_owner)}/owned-weapons/{Id(weapon)}";

        using var noVersion = await _registry.FiringChief.PutAsJsonAsync(
            url, new { weaponModelId = _arcabuz.Id, weaponNumber = "1", ownershipGuideNumber = "SINT-1022" }, TestContext.Current.CancellationToken);
        using var unknownMember = await _registry.FiringChief.PostAsJsonAsync(
            $"/api/arquebusiers/{Id(_owner)}/owned-weapons",
            new { weaponModelId = _arcabuz.Id, weaponNumber = "1", ownershipGuideNumber = "SINT-1023", serialNumber = "X" },
            TestContext.Current.CancellationToken);

        Assert.Equal("required", (await ErrorsAsync(noVersion))["version"]);
        Assert.Equal(HttpStatusCode.BadRequest, unknownMember.StatusCode);
    }

    [Fact]
    public async Task A_weapon_that_does_not_exist_under_an_arquebusier_in_scope_is_not_found()
    {
        using var response = await _registry.FiringChief.DeleteAsync(
            $"/api/arquebusiers/{Id(_owner)}/owned-weapons/{Guid.CreateVersion7()}", TestContext.Current.CancellationToken);

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "ownedWeapons.notFound");
    }

    [Fact]
    public async Task Weapons_of_an_arquebusier_outside_the_scope_or_of_another_arquebusier_do_not_exist()
    {
        var other = await _registry.RegisterAsync(_registry.Other.Id);
        var foreignWeapon = await ReadAsync<JsonElement>(await AddAsync(_registry.Admin, Id(other), _arcabuz.Id, "SINT-1008"));
        var sibling = await _registry.RegisterAsync(_registry.Own.Id);

        using var add = await AddAsync(_registry.FiringChief, Id(other), _arcabuz.Id, "SINT-1009");
        using var edit = await EditAsync(_registry.FiringChief, Id(other), foreignWeapon, _arcabuz.Id, "SINT-1008", weaponNumber: "9");
        using var remove = await _registry.FiringChief.DeleteAsync($"/api/arquebusiers/{Id(other)}/owned-weapons/{Id(foreignWeapon)}", TestContext.Current.CancellationToken);
        using var wrongParent = await _registry.Admin.DeleteAsync($"/api/arquebusiers/{Id(sibling)}/owned-weapons/{Id(foreignWeapon)}", TestContext.Current.CancellationToken);

        await AssertProblemAsync(add, HttpStatusCode.NotFound, "arquebusiers.notFound");
        await AssertProblemAsync(edit, HttpStatusCode.NotFound, "arquebusiers.notFound");
        await AssertProblemAsync(remove, HttpStatusCode.NotFound, "arquebusiers.notFound");
        await AssertProblemAsync(wrongParent, HttpStatusCode.NotFound, "ownedWeapons.notFound");
        var unchanged = Assert.Single((await DetailAsync(Id(other))).GetProperty("ownedWeapons").EnumerateArray());
        Assert.Equal(
            (foreignWeapon.GetProperty("weaponNumber").GetString(), foreignWeapon.GetProperty("version").GetUInt32()),
            (unchanged.GetProperty("weaponNumber").GetString(), unchanged.GetProperty("version").GetUInt32()));
    }

    [Fact]
    public async Task Weapon_changes_are_audited_without_numbers()
    {
        var weapon = await ReadAsync<JsonElement>(await AddAsync(_registry.FiringChief, Id(_owner), _arcabuz.Id, "SINT-1010", weaponNumber: "5555"));
        var edited = await ReadAsync<JsonElement>(await EditAsync(_registry.FiringChief, Id(_owner), weapon, _arcabuz.Id, "SINT-1011", weaponNumber: "6666"));
        using var unchanged = await EditAsync(_registry.FiringChief, Id(_owner), edited, _arcabuz.Id, "SINT-1011", weaponNumber: "6666");
        using var removed = await _registry.FiringChief.DeleteAsync($"/api/arquebusiers/{Id(_owner)}/owned-weapons/{Id(weapon)}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        var added = Assert.Single(await _registry.Host.AuditEntriesAsync("OwnedWeaponAdded"));
        var updated = Assert.Single(await _registry.Host.AuditEntriesAsync("OwnedWeaponUpdated"));
        var deleted = Assert.Single(await _registry.Host.AuditEntriesAsync("OwnedWeaponRemoved"));
        foreach (var entry in new[] { added, updated, deleted })
        {
            Assert.Equal((_registry.FiringChiefId, _registry.Own.Id, "OwnedWeapon", Id(weapon)), (entry.ActorUserId, entry.ComparsaId, entry.EntityType, entry.EntityId));
            foreach (var secret in new[] { "SINT-1010", "SINT-1011", "5555", "6666" })
            {
                Assert.DoesNotContain(secret, entry.Data ?? string.Empty, StringComparison.Ordinal);
            }
        }

        using var data = JsonDocument.Parse(updated.Data!);
        Assert.Equal(["weaponNumber", "ownershipGuideNumber"], data.RootElement.GetProperty("changedFields").EnumerateArray().Select(f => f.GetString()));
        Assert.Empty((await DetailAsync(Id(_owner))).GetProperty("ownedWeapons").EnumerateArray());
    }

    private async Task DeactivateAsync(Guid modelId)
    {
        using var response = await _registry.Admin.PostAsync($"/api/weapon-models/{modelId}/deactivate", new { });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<JsonElement> DetailAsync(string id) =>
        await ReadAsync<JsonElement>(await _registry.Admin.GetAsync($"/api/arquebusiers/{id}", TestContext.Current.CancellationToken));

    private static string Id(JsonElement element) => element.GetProperty("id").GetString()!;

    private static Task<HttpResponseMessage> AddAsync(HttpClient client, string arquebusierId, Guid modelId, string guide, string weaponNumber = "1234") =>
        client.PostAsJsonAsync(
            $"/api/arquebusiers/{arquebusierId}/owned-weapons",
            new { weaponModelId = modelId, weaponNumber, ownershipGuideNumber = guide },
            TestContext.Current.CancellationToken);

    private static Task<HttpResponseMessage> EditAsync(
        HttpClient client, string arquebusierId, JsonElement weapon, Guid modelId, string guide, string weaponNumber = "1234") =>
        client.PutAsJsonAsync(
            $"/api/arquebusiers/{arquebusierId}/owned-weapons/{Id(weapon)}",
            new { weaponModelId = modelId, weaponNumber, ownershipGuideNumber = guide, version = weapon.GetProperty("version").GetUInt32() },
            TestContext.Current.CancellationToken);
}
