using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PolvorApp.Api.Tests.Infrastructure;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Registry;

/// <summary>
/// Spec "Registering and editing arquebusiers (UC-01, UC-02)" (editing, outdated versions, no-ops),
/// "Current license", "Training course", "Active and Reserve status" and "Registry changes are audited".
/// </summary>
public sealed class ArquebusierEditingTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private RegistryTestHost _registry = null!;
    private JsonElement _arquebusier;

    public async ValueTask InitializeAsync()
    {
        _registry = await RegistryTestHost.StartAsync(postgres, mailpit);
        _arquebusier = await _registry.RegisterAsync(_registry.Own.Id);
    }

    public async ValueTask DisposeAsync() => await _registry.DisposeAsync();

    [Fact]
    public async Task A_FiringChief_edits_personal_data_license_course_and_status()
    {
        var body = EditOf(_arquebusier);
        (body["phone"], body["status"], body["trainingCompletedOn"]) = ("+34 611 000 002", "RESERVE", "2026-01-20");
        body["license"] = new Dictionary<string, object?> { ["type"] = "A_PROF", ["pending"] = false, ["issuedOn"] = "2026-02-01" };

        using var response = await PutAsync(_registry.FiringChief, Id(_arquebusier), body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var edited = await ReadAsync<JsonElement>(response);
        Assert.Equal(("+34 611 000 002", "RESERVE", "2026-01-20"), (edited.GetProperty("phone").GetString(), edited.GetProperty("status").GetString(), edited.GetProperty("trainingCompletedOn").GetString()));
        Assert.Equal(("A_PROF", "2027-02-01"), (edited.GetProperty("license").GetProperty("type").GetString(), edited.GetProperty("license").GetProperty("expiresOn").GetString()));
        Assert.NotEqual(_arquebusier.GetProperty("version").GetUInt32(), edited.GetProperty("version").GetUInt32());
    }

    [Fact]
    public async Task An_edit_clears_omitted_optional_fields()
    {
        var body = EditOf(_arquebusier);
        body.Remove("email");
        body.Remove("license");
        body.Remove("trainingCompletedOn");

        using var response = await PutAsync(_registry.Admin, Id(_arquebusier), body);

        var edited = await ReadAsync<JsonElement>(response);
        Assert.Equal(_registry.Own.Id, edited.GetProperty("comparsaId").GetGuid());
        Assert.Equal(JsonValueKind.Null, edited.GetProperty("email").ValueKind);
        Assert.Equal(JsonValueKind.Null, edited.GetProperty("license").ValueKind);
        Assert.Equal(JsonValueKind.Null, edited.GetProperty("trainingCompletedOn").ValueKind);
    }

    [Theory]
    [InlineData("comparsaId")]
    [InlineData("phoneNumber")]
    [InlineData("license.expiryDate")]
    public async Task Unknown_members_are_rejected_so_a_typo_never_clears_data(string member)
    {
        var body = EditOf(_arquebusier);
        if (member.StartsWith("license.", StringComparison.Ordinal))
        {
            ((Dictionary<string, object?>)body["license"]!)[member["license.".Length..]] = "2030-01-01";
        }
        else
        {
            body[member] = member == "comparsaId" ? _registry.Other.Id : "+34 600 000 009";
        }

        using var response = await PutAsync(_registry.Admin, Id(_arquebusier), body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var current = await ReadAsync<JsonElement>(await _registry.Admin.GetAsync($"/api/arquebusiers/{Id(_arquebusier)}", TestContext.Current.CancellationToken));
        Assert.Equal(_arquebusier.GetProperty("version").GetUInt32(), current.GetProperty("version").GetUInt32());
        Assert.Equal(_registry.Own.Id, current.GetProperty("comparsaId").GetGuid());
    }

    [Fact]
    public async Task A_duplicate_federation_id_on_edit_is_blocking()
    {
        var other = await _registry.RegisterAsync(_registry.Other.Id);
        var body = EditOf(_arquebusier);
        body["federationId"] = other.GetProperty("federationId").GetInt32();

        using var response = await PutAsync(_registry.Admin, Id(_arquebusier), body);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "arquebusiers.federationIdTaken");
    }

    [Fact]
    public async Task An_edit_based_on_an_outdated_version_is_rejected()
    {
        var first = EditOf(_arquebusier);
        first["phone"] = "+34 622 000 003";
        var second = EditOf(_arquebusier);
        second["phone"] = "+34 633 000 004";

        using var saved = await PutAsync(_registry.FiringChief, Id(_arquebusier), first);
        using var stale = await PutAsync(_registry.Admin, Id(_arquebusier), second);

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        await AssertProblemAsync(stale, HttpStatusCode.Conflict, "arquebusiers.modified");
        var current = await ReadAsync<JsonElement>(await _registry.Admin.GetAsync($"/api/arquebusiers/{Id(_arquebusier)}", TestContext.Current.CancellationToken));
        Assert.Equal("+34 622 000 003", current.GetProperty("phone").GetString());
    }

    [Fact]
    public async Task The_version_and_the_status_are_required()
    {
        var body = EditOf(_arquebusier);
        body.Remove("version");
        body.Remove("status");

        using var response = await PutAsync(_registry.Admin, Id(_arquebusier), body);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation");
        var errors = await ErrorsAsync(response);
        Assert.Equal(("required", "required"), (errors["version"], errors["status"]));
    }

    [Fact]
    public async Task An_unchanged_edit_saves_and_audits_nothing()
    {
        using var response = await PutAsync(_registry.FiringChief, Id(_arquebusier), EditOf(_arquebusier));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(_arquebusier.GetProperty("version").GetUInt32(), (await ReadAsync<JsonElement>(response)).GetProperty("version").GetUInt32());
        Assert.Empty(await _registry.Host.AuditEntriesAsync("ArquebusierUpdated"));
    }

    [Fact]
    public async Task An_edit_is_audited_with_the_changed_field_names_only()
    {
        var body = EditOf(_arquebusier);
        var (nationalId, _) = RegistryData.NextIdentity();
        (body["nationalId"], body["phone"]) = (nationalId, "+34 644 000 005");

        using var response = await PutAsync(_registry.FiringChief, Id(_arquebusier), body);

        var entry = Assert.Single(await _registry.Host.AuditEntriesAsync("ArquebusierUpdated"));
        Assert.Equal((_registry.FiringChiefId, _registry.Own.Id), (entry.ActorUserId, entry.ComparsaId));
        using var data = JsonDocument.Parse(entry.Data!);
        Assert.Equal(["nationalId", "phone"], data.RootElement.GetProperty("changedFields").EnumerateArray().Select(f => f.GetString()));
        ArquebusierRegistrationTests.AssertNoPersonalValues(entry.Data, await ReadAsync<JsonElement>(response));
        ArquebusierRegistrationTests.AssertNoPersonalValues(entry.Data, _arquebusier);
    }

    [Fact]
    public async Task A_duplicate_national_id_on_edit_is_blocking()
    {
        var other = await _registry.RegisterAsync(_registry.Other.Id);
        var body = EditOf(_arquebusier);
        body["nationalId"] = other.GetProperty("nationalId").GetString();

        using var response = await PutAsync(_registry.FiringChief, Id(_arquebusier), body);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "arquebusiers.nationalIdTaken");
    }

    [Fact]
    public async Task An_arquebusier_of_an_inactive_comparsa_stays_editable()
    {
        var inInactive = RegistryData.NewArquebusier(_registry.Inactive.Id);
        await _registry.Services.SaveRegistryAsync(inInactive);
        var current = await ReadAsync<JsonElement>(await _registry.FiringChief.GetAsync($"/api/arquebusiers/{inInactive.Id}", TestContext.Current.CancellationToken));
        var body = EditOf(current);
        body["phone"] = "+34 655 000 006";

        using var response = await PutAsync(_registry.FiringChief, inInactive.Id.ToString(), body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task An_arquebusier_outside_the_scope_cannot_be_edited()
    {
        var other = await _registry.RegisterAsync(_registry.Other.Id);
        var body = EditOf(other);
        body["phone"] = "+34 666 000 007";

        using var response = await PutAsync(_registry.FiringChief, Id(other), body);

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "arquebusiers.notFound");
        Assert.Empty(await _registry.Host.AuditEntriesAsync("ArquebusierUpdated"));
    }

    [Fact]
    public async Task Invalid_fields_on_edit_are_named()
    {
        var body = EditOf(_arquebusier);
        body["nationalId"] = "12345678A";

        using var response = await PutAsync(_registry.Admin, Id(_arquebusier), body);

        Assert.Equal("checkLetter", (await ErrorsAsync(response))["nationalId"]);
    }

    /// <summary>An edit body that repeats every current value of <paramref name="arquebusier"/>.</summary>
    internal static Dictionary<string, object?> EditOf(JsonElement arquebusier)
    {
        var license = arquebusier.GetProperty("license");
        return new Dictionary<string, object?>
        {
            ["federationId"] = arquebusier.GetProperty("federationId").GetInt32(),
            ["nationalId"] = arquebusier.GetProperty("nationalId").GetString(),
            ["firstName"] = arquebusier.GetProperty("firstName").GetString(),
            ["lastName"] = arquebusier.GetProperty("lastName").GetString(),
            ["birthDate"] = arquebusier.GetProperty("birthDate").GetString(),
            ["email"] = StringOrNull(arquebusier.GetProperty("email")),
            ["phone"] = StringOrNull(arquebusier.GetProperty("phone")),
            ["gender"] = arquebusier.GetProperty("gender").GetString(),
            ["status"] = arquebusier.GetProperty("status").GetString(),
            ["trainingCompletedOn"] = StringOrNull(arquebusier.GetProperty("trainingCompletedOn")),
            ["license"] = license.ValueKind == JsonValueKind.Null ? null : new Dictionary<string, object?>
            {
                ["type"] = license.GetProperty("type").GetString(),
                ["pending"] = license.GetProperty("pending").GetBoolean(),
                ["issuedOn"] = StringOrNull(license.GetProperty("issuedOn")),
                ["expiresOn"] = StringOrNull(license.GetProperty("expiresOn")),
            },
            ["version"] = arquebusier.GetProperty("version").GetUInt32(),
        };
    }

    private static string? StringOrNull(JsonElement value) => value.ValueKind == JsonValueKind.Null ? null : value.GetString();

    private static string Id(JsonElement arquebusier) => arquebusier.GetProperty("id").GetString()!;

    private static Task<HttpResponseMessage> PutAsync(HttpClient client, string id, Dictionary<string, object?> body) =>
        client.PutAsJsonAsync($"/api/arquebusiers/{id}", body, TestContext.Current.CancellationToken);
}
