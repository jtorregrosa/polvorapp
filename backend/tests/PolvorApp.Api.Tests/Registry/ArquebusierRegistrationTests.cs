using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.ArquebusierRegistry.Persistence;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Registry;

/// <summary>
/// Spec "Registering and editing arquebusiers (UC-01, UC-02)" (registration), "Arquebusier data",
/// "Federation-wide uniqueness (BR-02)" and "Registry changes are audited" (registration).
/// </summary>
[Collection(PostgresGroup.Name)]
public sealed class ArquebusierRegistrationTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private static readonly string[] ExpectedInvalidFields = ["birthDate", "comparsaId", "email", "federationId", "firstName", "gender"];
    private RegistryTestHost _registry = null!;

    public async ValueTask InitializeAsync() => _registry = await RegistryTestHost.StartAsync(postgres, mailpit);

    public async ValueTask DisposeAsync() => await _registry.DisposeAsync();

    [Fact]
    public async Task A_FiringChief_registers_an_arquebusier_in_their_comparsa()
    {
        var body = RegistryTestHost.NewArquebusier(_registry.Own.Id);
        body["nationalId"] = " 12345678-z ";

        using var response = await _registry.FiringChief.PostAsync("/api/arquebusiers", body);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await ReadAsync<JsonElement>(response);
        var id = created.GetProperty("id").GetGuid();
        Assert.Equal($"/api/arquebusiers/{id}", response.Headers.Location?.OriginalString);
        Assert.Equal("12345678Z", created.GetProperty("nationalId").GetString());
        Assert.Equal("ACTIVE", created.GetProperty("status").GetString());
        Assert.Equal(_registry.Own.Name, created.GetProperty("comparsaName").GetString());
        Assert.Equal(RegistryTestHost.Iso(RegistryTestHost.DefaultExpiresOn), created.GetProperty("license").GetProperty("expiresOn").GetString());
        Assert.Equal("VALID", created.GetProperty("license").GetProperty("status").GetString());
        Assert.Equal(0, created.GetProperty("ownedWeapons").GetArrayLength());
        Assert.True(created.GetProperty("version").GetUInt32() > 0);
    }

    [Fact]
    public async Task An_Admin_registers_in_any_comparsa()
    {
        using var response = await _registry.Admin.PostAsync("/api/arquebusiers", RegistryTestHost.NewArquebusier(_registry.Other.Id));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task A_comparsa_outside_the_scope_does_not_exist_for_the_FiringChief()
    {
        using var response = await _registry.FiringChief.PostAsync("/api/arquebusiers", RegistryTestHost.NewArquebusier(_registry.Other.Id));

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "arquebusiers.comparsaNotFound");
        Assert.Equal(0, await CountAsync());
    }

    [Fact]
    public async Task An_unknown_comparsa_is_not_found()
    {
        using var response = await _registry.Admin.PostAsync("/api/arquebusiers", RegistryTestHost.NewArquebusier(Guid.CreateVersion7()));

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "arquebusiers.comparsaNotFound");
    }

    [Fact]
    public async Task Registering_in_an_inactive_comparsa_is_blocking()
    {
        using var asChief = await _registry.FiringChief.PostAsync("/api/arquebusiers", RegistryTestHost.NewArquebusier(_registry.Inactive.Id));
        using var asAdmin = await _registry.Admin.PostAsync("/api/arquebusiers", RegistryTestHost.NewArquebusier(_registry.Inactive.Id));

        await AssertProblemAsync(asChief, HttpStatusCode.Conflict, "arquebusiers.comparsaInactive");
        await AssertProblemAsync(asAdmin, HttpStatusCode.Conflict, "arquebusiers.comparsaInactive");
        Assert.Equal(0, await CountAsync());
    }

    [Fact]
    public async Task A_duplicate_national_id_in_another_comparsa_reveals_nothing_about_it()
    {
        var existing = await _registry.RegisterAsync(_registry.Other.Id, body => body["lastName"] = "Ajena Oculta");
        var body = RegistryTestHost.NewArquebusier(_registry.Own.Id);
        body["nationalId"] = existing.GetProperty("nationalId").GetString()!.ToLowerInvariant();

        using var response = await _registry.FiringChief.PostAsync("/api/arquebusiers", body);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "arquebusiers.nationalIdTaken");
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain(existing.GetProperty("id").GetString()!, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Ajena", text, StringComparison.Ordinal);
        Assert.DoesNotContain(_registry.Other.Id.ToString(), text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_duplicate_federation_id_is_blocking()
    {
        var existing = await _registry.RegisterAsync(_registry.Own.Id);
        var body = RegistryTestHost.NewArquebusier(_registry.Own.Id);
        body["federationId"] = existing.GetProperty("federationId").GetInt32();

        using var response = await _registry.Admin.PostAsync("/api/arquebusiers", body);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "arquebusiers.federationIdTaken");
        Assert.Equal(1, await CountAsync());
    }

    [Fact]
    public async Task Invalid_fields_are_named_and_nothing_is_stored()
    {
        var body = RegistryTestHost.NewArquebusier(_registry.Own.Id);
        body["firstName"] = "";
        body["birthDate"] = _registry.Today.AddDays(1).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        body["federationId"] = 0;
        body["email"] = "not-an-email";
        body["gender"] = "OTHER";
        body.Remove("comparsaId");

        using var response = await _registry.Admin.PostAsync("/api/arquebusiers", body);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation");
        var errors = await ErrorsAsync(response);
        Assert.Equal(ExpectedInvalidFields, errors.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(0, await CountAsync());
    }

    [Fact]
    public async Task Registration_is_audited_with_the_comparsa_and_no_personal_values()
    {
        var created = await _registry.RegisterAsync(_registry.Own.Id);

        var entry = Assert.Single(await _registry.Host.AuditEntriesAsync("ArquebusierRegistered"));
        Assert.Equal(
            (_registry.AdminId, _registry.Own.Id, "Arquebusier", created.GetProperty("id").GetString()),
            (entry.ActorUserId, entry.ComparsaId, entry.EntityType, entry.EntityId));
        AssertNoPersonalValues(entry.Data, created);
    }

    /// <summary>The audit data must not contain any personal value of the arquebusier (D7, BR-14).</summary>
    internal static void AssertNoPersonalValues(string? data, JsonElement arquebusier)
    {
        foreach (var field in new[] { "nationalId", "firstName", "lastName", "birthDate", "email", "phone" })
        {
            if (arquebusier.GetProperty(field).ValueKind == JsonValueKind.String)
            {
                Assert.DoesNotContain(arquebusier.GetProperty(field).GetString()!, data ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            }
        }

        Assert.DoesNotContain(arquebusier.GetProperty("federationId").GetInt32().ToString(System.Globalization.CultureInfo.InvariantCulture), data ?? string.Empty, StringComparison.Ordinal);
    }

    private async Task<int> CountAsync()
    {
        await using var scope = _registry.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ArquebusierRegistryDbContext>().Arquebusiers.CountAsync(TestContext.Current.CancellationToken);
    }
}

/// <summary>BR-02 under concurrency: both requests pass the pre-check, the unique index decides.</summary>
[Collection(PostgresGroup.Name)]
public sealed class ArquebusierRegistrationRaceTests(PostgresFixture postgres, MailpitFixture mailpit)
{
    [Fact]
    public async Task Two_registrations_of_the_same_national_id_at_once_store_one()
    {
        BarrierAuditTrail barrier = null!;
        await using var registry = await RegistryTestHost.StartAsync(
            postgres, mailpit, services => barrier = BarrierAuditTrail.Decorate(services, "ArquebusierRegistered", parties: 2));
        var first = RegistryTestHost.NewArquebusier(registry.Own.Id);
        var second = RegistryTestHost.NewArquebusier(registry.Other.Id);
        second["nationalId"] = first["nationalId"];

        var responses = await Task.WhenAll(new[] { first, second }.Select(body => registry.Admin.PostAsync("/api/arquebusiers", body)));

        Assert.Equal(2, barrier.Arrived);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        await AssertProblemAsync(Assert.Single(responses, r => r.StatusCode != HttpStatusCode.Created), HttpStatusCode.Conflict, "arquebusiers.nationalIdTaken");
        Assert.Single(await registry.Host.AuditEntriesAsync("ArquebusierRegistered"));
    }
}
