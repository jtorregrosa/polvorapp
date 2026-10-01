using System.Net;
using System.Text.Json;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.IdentityAccess.Contracts;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Registry;

/// <summary>
/// Spec "Arquebusier visibility (BR-12)" and "Current license" (derived status): scoped lists and
/// details, filters, Spanish order and the detail shape.
/// </summary>
[Collection(PostgresGroup.Name)]
public sealed class ArquebusierVisibilityTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private RegistryTestHost _registry = null!;
    private JsonElement _own;
    private JsonElement _reserveOwn;
    private JsonElement _inInactive;
    private JsonElement _other;

    public async ValueTask InitializeAsync()
    {
        _registry = await RegistryTestHost.StartAsync(postgres, mailpit);
        _own = await _registry.RegisterAsync(_registry.Own.Id, body => (body["lastName"], body["firstName"]) = ("Ñúñez", "Beatriz"));
        _reserveOwn = await _registry.RegisterAsync(_registry.Own.Id, body =>
        {
            (body["lastName"], body["firstName"], body["status"]) = ("Nuñez", "Alba", "RESERVE");
            body["license"] = new Dictionary<string, object?> { ["type"] = "A_PROF", ["pending"] = true };
        });
        _other = await _registry.RegisterAsync(_registry.Other.Id, body =>
        {
            (body["lastName"], body["firstName"]) = ("Álvarez", "Carmen");
            body["license"] = new Dictionary<string, object?> { ["type"] = "AE", ["pending"] = false, ["issuedOn"] = "2015-01-01" };
        });

        // The inactive comparsa takes no new arquebusiers: this one was registered before it was deactivated.
        var inInactive = RegistryData.NewArquebusier(_registry.Inactive.Id, lastName: "Zapata");
        await _registry.Services.SaveRegistryAsync(inInactive);
        _inInactive = await ReadAsync<JsonElement>(await _registry.Admin.GetAsync($"/api/arquebusiers/{inInactive.Id}", TestContext.Current.CancellationToken));
    }

    public async ValueTask DisposeAsync() => await _registry.DisposeAsync();

    [Fact]
    public async Task An_Admin_sees_every_arquebusier_in_Spanish_order()
    {
        var rows = await ListAsync(_registry.Admin, string.Empty);

        Assert.Equal(["Álvarez", "Nuñez", "Ñúñez", "Zapata"], rows.Select(r => r.GetProperty("lastName").GetString()));
    }

    [Fact]
    public async Task A_FiringChief_sees_only_their_comparsas_active_or_not()
    {
        var rows = await ListAsync(_registry.FiringChief, string.Empty);

        Assert.Equal(
            new[] { Id(_reserveOwn), Id(_own), Id(_inInactive) },
            rows.Select(r => r.GetProperty("id").GetString()));
    }

    [Fact]
    public async Task Lists_filter_by_comparsa_and_status()
    {
        var reserveInOwn = await ListAsync(_registry.Admin, $"?comparsaId={_registry.Own.Id}&status=RESERVE");
        var foreignForChief = await ListAsync(_registry.FiringChief, $"?comparsaId={_registry.Other.Id}");

        Assert.Equal([Id(_reserveOwn)], reserveInOwn.Select(r => r.GetProperty("id").GetString()));
        Assert.Empty(foreignForChief);
    }

    [Fact]
    public async Task An_unknown_status_filter_is_invalid()
    {
        using var response = await _registry.Admin.GetAsync("/api/arquebusiers?status=INACTIVE", TestContext.Current.CancellationToken);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation");
        Assert.Equal("invalid", (await ErrorsAsync(response))["status"]);
    }

    [Fact]
    public async Task Rows_carry_the_derived_license_status()
    {
        var rows = (await ListAsync(_registry.Admin, string.Empty)).ToDictionary(r => r.GetProperty("id").GetString()!);

        Assert.Equal("VALID", rows[Id(_own)].GetProperty("licenseStatus").GetString());
        Assert.Equal(RegistryTestHost.Iso(RegistryTestHost.DefaultExpiresOn), rows[Id(_own)].GetProperty("licenseExpiresOn").GetString());
        Assert.Equal("PENDING", rows[Id(_reserveOwn)].GetProperty("licenseStatus").GetString());
        Assert.Equal("EXPIRED", rows[Id(_other)].GetProperty("licenseStatus").GetString());
        Assert.Equal(JsonValueKind.Null, rows[Id(_inInactive)].GetProperty("licenseStatus").ValueKind);
        Assert.Equal(_registry.Own.Name, rows[Id(_own)].GetProperty("comparsaName").GetString());
    }

    [Fact]
    public async Task A_FiringChief_without_assignments_sees_nothing()
    {
        using var lonely = await _registry.Host.SignInAsync(await _registry.Host.CreateUserAsync("jefe.sin.comparsa@example.test", UserRole.FiringChief));

        Assert.Empty(await ListAsync(lonely, string.Empty));
    }

    [Fact]
    public async Task An_arquebusier_outside_the_scope_does_not_exist_for_the_FiringChief()
    {
        using var foreign = await _registry.FiringChief.GetAsync($"/api/arquebusiers/{Id(_other)}", TestContext.Current.CancellationToken);
        using var unknown = await _registry.Admin.GetAsync($"/api/arquebusiers/{Guid.CreateVersion7()}", TestContext.Current.CancellationToken);

        await AssertProblemAsync(foreign, HttpStatusCode.NotFound, "arquebusiers.notFound");
        await AssertProblemAsync(unknown, HttpStatusCode.NotFound, "arquebusiers.notFound");
    }

    [Fact]
    public async Task The_detail_carries_the_comparsa_the_owned_weapons_and_the_version()
    {
        var model = RegistryData.NewWeaponModel("PISTOLA SINTÉTICA", WeaponKind.Pistol);
        await _registry.Services.SaveCatalogAsync(model);
        await _registry.Services.SaveRegistryAsync(RegistryData.NewOwnedWeapon(Guid.Parse(Id(_own)), model.Id, "SINT-0800"));

        var detail = await ReadAsync<JsonElement>(await _registry.FiringChief.GetAsync($"/api/arquebusiers/{Id(_own)}", TestContext.Current.CancellationToken));

        Assert.Equal((_registry.Own.Name, true), (detail.GetProperty("comparsaName").GetString(), detail.GetProperty("comparsaActive").GetBoolean()));
        var weapon = Assert.Single(detail.GetProperty("ownedWeapons").EnumerateArray());
        Assert.Equal(("PISTOLA SINTÉTICA", "PISTOL"), (weapon.GetProperty("model").GetProperty("label").GetString(), weapon.GetProperty("model").GetProperty("kind").GetString()));
        Assert.Equal("SINT-0800", weapon.GetProperty("ownershipGuideNumber").GetString());
        Assert.Equal("1990-05-01", detail.GetProperty("birthDate").GetString());
        Assert.True(detail.GetProperty("version").GetUInt32() > 0);
        Assert.False(_inInactive.GetProperty("comparsaActive").GetBoolean());
    }

    private static string Id(JsonElement arquebusier) => arquebusier.GetProperty("id").GetString()!;

    private static async Task<List<JsonElement>> ListAsync(HttpClient client, string query)
    {
        using var response = await client.GetAsync("/api/arquebusiers" + query, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return [.. (await ReadAsync<JsonElement>(response)).EnumerateArray()];
    }
}
