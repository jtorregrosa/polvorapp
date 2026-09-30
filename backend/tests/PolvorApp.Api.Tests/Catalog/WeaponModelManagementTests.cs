using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.FederationCatalog.Endpoints;
using PolvorApp.IdentityAccess.Contracts;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Catalog;

/// <summary>Specs "Weapon models" (BR-07) and the model part of "Catalogue changes are audited".</summary>
[Collection(PostgresGroup.Name)]
public sealed class WeaponModelManagementTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private IdentityTestHost _host = null!;
    private SyntheticUser _adminUser = null!;
    private HttpClient _admin = null!;

    public async ValueTask InitializeAsync()
    {
        _host = await IdentityTestHost.StartAsync(postgres, mailpit);
        _adminUser = await _host.CreateUserAsync("admin.modelos@example.test", UserRole.Admin);
        _admin = await _host.SignInAsync(_adminUser);
    }

    public async ValueTask DisposeAsync()
    {
        _admin.Dispose();
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task An_admin_creates_a_rentable_model_and_it_is_audited_with_its_fields()
    {
        using var response = await _admin.PostAsync("/api/weapon-models", Trabuco("  TRABUCO CRISTIANO ZURDO (PEQUEÑO)  "));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var model = await ReadAsync<WeaponModelResponse>(response);
        Assert.Equal(
            new WeaponModelResponse(model.Id, WeaponKind.Trabuco, Side.Christian, Handedness.Left, WeaponSize.Small, true, "TRABUCO CRISTIANO ZURDO (PEQUEÑO)", true),
            model);
        Assert.Equal($"/api/weapon-models/{model.Id}", response.Headers.Location?.OriginalString);
        var entry = Assert.Single(await _host.AuditEntriesAsync("WeaponModelCreated"));
        Assert.Equal((_adminUser.Id, "WeaponModel", model.Id.ToString(), (Guid?)null), (entry.ActorUserId, entry.EntityType, entry.EntityId, entry.ComparsaId));
        using var data = JsonDocument.Parse(entry.Data!);
        Assert.Equal("LEFT", data.RootElement.GetProperty("handedness").GetString());
        Assert.True(data.RootElement.GetProperty("rentable").GetBoolean());
    }

    [Fact]
    public async Task A_pistol_needs_only_its_kind_and_label()
    {
        using var response = await _admin.PostAsync("/api/weapon-models", new { kind = "PISTOL", label = "PISTOLA", rentable = false });

        var model = await ReadAsync<WeaponModelResponse>(response);
        Assert.Equal((WeaponKind.Pistol, (Side?)null, (Handedness?)null, (WeaponSize?)null, false), (model.Kind, model.Side, model.Handedness, model.Size, model.Rentable));
    }

    [Fact]
    public async Task A_rentable_pistol_is_blocking_on_create_and_edit()
    {
        var pistol = await CreateAsync(new { kind = "PISTOL", label = "PISTOLA", rentable = false });

        using var create = await _admin.PostAsync("/api/weapon-models", new { kind = "PISTOL", label = "PISTOLA DE ALQUILER", rentable = true });
        using var edit = await _admin.PutAsJsonAsync($"/api/weapon-models/{pistol.Id}", new { kind = "PISTOL", label = "PISTOLA", rentable = true }, TestContext.Current.CancellationToken);

        foreach (var response in new[] { create, edit })
        {
            await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation");
            Assert.Equal("pistolNotRentable", (await ErrorsAsync(response))["rentable"]);
        }
    }

    [Theory]
    [InlineData("side")]
    [InlineData("handedness")]
    [InlineData("size")]
    public async Task A_trabuco_or_arcabuz_needs_every_attribute(string missing)
    {
        var body = new Dictionary<string, object?> { ["kind"] = "ARCABUZ", ["side"] = "MOORISH", ["handedness"] = "RIGHT", ["size"] = "NORMAL", ["rentable"] = true, ["label"] = "ARCABUZ INCOMPLETO" };
        body.Remove(missing);

        using var response = await _admin.PostAsync("/api/weapon-models", body);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation");
        Assert.Equal(new Dictionary<string, string> { [missing] = "required" }, await ErrorsAsync(response));
    }

    [Theory]
    [InlineData("kind", "CANNON")]
    [InlineData("side", "NEUTRAL")]
    [InlineData("handedness", "BOTH")]
    [InlineData("size", "LARGE")]
    public async Task Invalid_codes_are_named(string field, string value)
    {
        var body = new Dictionary<string, object?> { ["kind"] = "TRABUCO", ["side"] = "CHRISTIAN", ["handedness"] = "RIGHT", ["size"] = "NORMAL", ["rentable"] = true, ["label"] = "TRABUCO RARO" };
        body[field] = value;

        using var response = await _admin.PostAsync("/api/weapon-models", body);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation");
        Assert.Equal("invalid", (await ErrorsAsync(response))[field]);
    }

    [Theory]
    [InlineData(null, "required")]
    [InlineData("   ", "required")]
    [InlineData("PISTOLA\u200B", "invalid")]
    public async Task An_invalid_label_is_named(string? label, string reason)
    {
        using var response = await _admin.PostAsync("/api/weapon-models", new { kind = "PISTOL", label, rentable = false });

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation");
        Assert.Equal(reason, (await ErrorsAsync(response))["label"]);
    }

    [Fact]
    public async Task The_kind_is_required()
    {
        using var response = await _admin.PostAsync("/api/weapon-models", new { label = "SIN TIPO", rentable = false });

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation");
        Assert.Equal("required", (await ErrorsAsync(response))["kind"]);
    }

    [Fact]
    public async Task A_duplicate_label_ignoring_case_is_blocking()
    {
        await CreateAsync(new { kind = "PISTOL", label = "Pistola Pequeña", rentable = false });

        using var response = await _admin.PostAsync("/api/weapon-models", new { kind = "PISTOL", label = "PISTOLA PEQUEÑA", rentable = false });

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "weaponModels.labelTaken");
    }

    [Fact]
    public async Task A_duplicate_combination_is_blocking_on_create_and_edit()
    {
        await CreateAsync(Arcabuz("ARCABUZ MORO DIESTRO", "RIGHT"));
        var zurdo = await CreateAsync(Arcabuz("ARCABUZ MORO ZURDO", "LEFT"));

        using var create = await _admin.PostAsync("/api/weapon-models", Arcabuz("ARCABUZ MORO DIESTRO BIS", "RIGHT"));
        using var edit = await _admin.PutAsJsonAsync($"/api/weapon-models/{zurdo.Id}", Arcabuz("ARCABUZ MORO ZURDO", "RIGHT"), TestContext.Current.CancellationToken);

        await AssertProblemAsync(create, HttpStatusCode.Conflict, "weaponModels.combinationTaken");
        await AssertProblemAsync(edit, HttpStatusCode.Conflict, "weaponModels.combinationTaken");
        Assert.Empty(await _host.AuditEntriesAsync("WeaponModelUpdated"));
    }

    [Fact]
    public async Task The_kind_and_side_combine_freely()
    {
        var model = await CreateAsync(new { kind = "ARCABUZ", side = "CHRISTIAN", handedness = "RIGHT", size = "NORMAL", rentable = true, label = "ARCABUZ CRISTIANO DIESTRO" });

        Assert.Equal((WeaponKind.Arcabuz, Side.Christian), (model.Kind, model.Side));
    }

    [Fact]
    public async Task An_admin_edits_a_model_and_the_change_is_audited_with_previous_and_new_values()
    {
        var model = await CreateAsync(Trabuco("TRABUCO CRISTIANO ZURDO"));

        using var response = await _admin.PutAsJsonAsync(
            $"/api/weapon-models/{model.Id}",
            new { kind = "TRABUCO", side = "CHRISTIAN", handedness = "LEFT", size = "NORMAL", rentable = true, label = "TRABUCO CRISTIANO ZURDO (NORMAL)" },
            TestContext.Current.CancellationToken);

        var edited = await ReadAsync<WeaponModelResponse>(response);
        Assert.Equal(("TRABUCO CRISTIANO ZURDO (NORMAL)", WeaponSize.Normal), (edited.Label, edited.Size));
        var entry = Assert.Single(await _host.AuditEntriesAsync("WeaponModelUpdated"));
        using var data = JsonDocument.Parse(entry.Data!);
        Assert.Equal("SMALL", data.RootElement.GetProperty("previous").GetProperty("size").GetString());
        Assert.Equal("NORMAL", data.RootElement.GetProperty("current").GetProperty("size").GetString());
    }

    [Fact]
    public async Task An_unchanged_edit_is_not_audited()
    {
        var model = await CreateAsync(Trabuco("TRABUCO SIN CAMBIOS"));

        using var response = await _admin.PutAsJsonAsync($"/api/weapon-models/{model.Id}", Trabuco(" TRABUCO SIN CAMBIOS "), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await _host.AuditEntriesAsync("WeaponModelUpdated"));
    }

    [Fact]
    public async Task Editing_an_unknown_model_is_not_found()
    {
        using var response = await _admin.PutAsJsonAsync($"/api/weapon-models/{Guid.CreateVersion7()}", Trabuco("NADIE"), TestContext.Current.CancellationToken);

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "weaponModels.notFound");
    }

    [Fact]
    public async Task A_firing_chief_cannot_create_or_edit_models()
    {
        var model = await CreateAsync(Trabuco("TRABUCO PROTEGIDO"));
        using var chief = await _host.SignInAsync(await _host.CreateUserAsync("jefe.modelos@example.test"));

        using var create = await chief.PostAsync("/api/weapon-models", Trabuco("TRABUCO PIRATA"));
        using var edit = await chief.PutAsJsonAsync($"/api/weapon-models/{model.Id}", Trabuco("TRABUCO PIRATA"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, edit.StatusCode);
        Assert.Single(await _host.AuditEntriesAsync("WeaponModelCreated"));
        Assert.Empty(await _host.AuditEntriesAsync("WeaponModelUpdated"));
    }

    [Fact]
    public async Task Rentable_is_required_on_create_and_edit()
    {
        var model = await CreateAsync(Trabuco("TRABUCO ALQUILABLE"));

        using var create = await _admin.PostAsync("/api/weapon-models", new { kind = "PISTOL", label = "PISTOLA SIN FLAG" });
        using var edit = await _admin.PutAsJsonAsync(
            $"/api/weapon-models/{model.Id}", new { kind = "TRABUCO", side = "CHRISTIAN", handedness = "LEFT", size = "SMALL", label = "TRABUCO ALQUILABLE" }, TestContext.Current.CancellationToken);

        foreach (var response in new[] { create, edit })
        {
            await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation");
            Assert.Equal("required", (await ErrorsAsync(response))["rentable"]);
        }
    }

    [Fact]
    public async Task Invalid_and_missing_attributes_are_reported_together()
    {
        using var arcabuz = await _admin.PostAsync("/api/weapon-models", new { kind = "ARCABUZ", side = "NEUTRAL", size = "NORMAL", rentable = true, label = "ARCABUZ MIXTO" });
        using var empty = await _admin.PostAsync("/api/weapon-models", new { kind = "ARCABUZ", side = "", handedness = "RIGHT", size = "NORMAL", rentable = true, label = "ARCABUZ VACIO" });
        using var pistol = await _admin.PostAsync("/api/weapon-models", new { kind = "PISTOL", size = "LARGE", rentable = true, label = "PISTOLA MIXTA" });

        Assert.Equal(new Dictionary<string, string> { ["side"] = "invalid", ["handedness"] = "required" }, await ErrorsAsync(arcabuz));
        Assert.Equal(new Dictionary<string, string> { ["side"] = "required" }, await ErrorsAsync(empty));
        Assert.Equal(new Dictionary<string, string> { ["size"] = "invalid", ["rentable"] = "pistolNotRentable" }, await ErrorsAsync(pistol));
    }

    [Fact]
    public async Task An_empty_attribute_on_a_pistol_counts_as_absent_and_pistols_may_repeat_attributes()
    {
        var first = await CreateAsync(new { kind = "PISTOL", side = "", handedness = "", size = "", rentable = false, label = "PISTOLA A" });
        var second = await CreateAsync(new { kind = "PISTOL", rentable = false, label = "PISTOLA B" });

        Assert.Equal(((Side?)null, (Handedness?)null, (WeaponSize?)null), (first.Side, first.Handedness, first.Size));
        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public async Task Label_length_and_unicode_form_are_checked()
    {
        await CreateAsync(new { kind = "PISTOL", rentable = false, label = "PISTOLA ÉPICA" });

        using var tooLong = await _admin.PostAsync("/api/weapon-models", new { kind = "PISTOL", rentable = false, label = new string('x', 101) });
        using var decomposed = await _admin.PostAsync("/api/weapon-models", new { kind = "PISTOL", rentable = false, label = "PISTOLA E\u0301PICA" });

        Assert.Equal("tooLong", (await ErrorsAsync(tooLong))["label"]);
        await AssertProblemAsync(decomposed, HttpStatusCode.Conflict, "weaponModels.labelTaken");
        Assert.Single(await _host.AuditEntriesAsync("WeaponModelCreated"));
    }

    [Fact]
    public async Task An_edit_keeps_the_active_state_and_can_turn_a_trabuco_into_a_pistol()
    {
        var model = await CreateAsync(Trabuco("TRABUCO RETIRADO"));
        (await _admin.PostAsync($"/api/weapon-models/{model.Id}/deactivate", new { })).Dispose();

        using var unchanged = await _admin.PutAsJsonAsync($"/api/weapon-models/{model.Id}", Trabuco("TRABUCO RETIRADO"), TestContext.Current.CancellationToken);
        using var toPistol = await _admin.PutAsJsonAsync($"/api/weapon-models/{model.Id}", new { kind = "PISTOL", rentable = false, label = "PISTOLA RETIRADA" }, TestContext.Current.CancellationToken);

        Assert.False((await ReadAsync<WeaponModelResponse>(unchanged)).Active);
        var pistol = await ReadAsync<WeaponModelResponse>(toPistol);
        Assert.Equal((WeaponKind.Pistol, (Side?)null, false, false), (pistol.Kind, pistol.Side, pistol.Rentable, pistol.Active));
        var entry = Assert.Single(await _host.AuditEntriesAsync("WeaponModelUpdated"));
        using var data = JsonDocument.Parse(entry.Data!);
        Assert.False(data.RootElement.GetProperty("current").TryGetProperty("active", out _));
        Assert.Equal("TRABUCO RETIRADO", data.RootElement.GetProperty("previous").GetProperty("label").GetString());
        Assert.Equal(JsonValueKind.Null, data.RootElement.GetProperty("current").GetProperty("side").ValueKind);
    }

    [Fact]
    public async Task Writes_need_a_signed_in_user()
    {
        var model = await CreateAsync(Trabuco("TRABUCO ANONIMO"));
        using var anonymous = await _host.NewClientAsync();

        using var create = await anonymous.PostAsync("/api/weapon-models", Trabuco("TRABUCO INTRUSO"));
        using var delete = await anonymous.DeleteAsync($"/api/weapon-models/{model.Id}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, create.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, delete.StatusCode);
    }

    private static object Trabuco(string label) =>
        new { kind = "TRABUCO", side = "CHRISTIAN", handedness = "LEFT", size = "SMALL", rentable = true, label };

    private static object Arcabuz(string label, string handedness) =>
        new { kind = "ARCABUZ", side = "MOORISH", handedness, size = "NORMAL", rentable = true, label };

    private async Task<WeaponModelResponse> CreateAsync(object body)
    {
        using var response = await _admin.PostAsync("/api/weapon-models", body);
        return await ReadAsync<WeaponModelResponse>(response);
    }
}
