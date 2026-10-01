using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.SharedKernel.Auditing;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Registry;

/// <summary>
/// Spec "Registry changes are audited" (in the same transaction, field names only) and design D10
/// (concurrent writes and lock timeouts) across every registry write.
/// </summary>
[Collection(PostgresGroup.Name)]
public sealed class RegistryIntegrityTests(PostgresFixture postgres, MailpitFixture mailpit)
{
    private static readonly string[] TwoPhones = ["+34 600 000 031", "+34 600 000 032"];
    private static readonly string[] TwoWeaponNumbers = ["41", "42"];

    [Fact]
    public async Task An_edit_of_every_field_names_each_one_and_audits_no_value()
    {
        await using var registry = await RegistryTestHost.StartAsync(postgres, mailpit);
        var before = await registry.RegisterAsync(registry.Own.Id);
        var body = ArquebusierEditingTests.EditOf(before);
        var (nationalId, federationId) = RegistryData.NextIdentity();
        body["federationId"] = federationId;
        body["nationalId"] = nationalId;
        body["firstName"] = "Arcabucero";
        body["lastName"] = "Sintético Cambiado";
        body["birthDate"] = "1985-07-21";
        body["email"] = "cambiado@polvorapp.example";
        body["phone"] = "+34 699 000 021";
        body["gender"] = "MALE";
        body["status"] = "RESERVE";
        body["trainingCompletedOn"] = "2024-12-02";
        body["license"] = new Dictionary<string, object?> { ["type"] = "A_PROF", ["pending"] = false, ["issuedOn"] = "2025-06-18" };

        using var response = await registry.FiringChief.PutAsJsonAsync($"/api/arquebusiers/{Id(before)}", body, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var after = await ReadAsync<JsonElement>(response);
        var entry = Assert.Single(await registry.Host.AuditEntriesAsync("ArquebusierUpdated"));
        using var data = JsonDocument.Parse(entry.Data!);
        Assert.Equal(
            ["federationId", "nationalId", "firstName", "lastName", "birthDate", "email", "phone", "gender", "status", "trainingCompletedOn", "license"],
            data.RootElement.GetProperty("changedFields").EnumerateArray().Select(field => field.GetString()));
        ArquebusierRegistrationTests.AssertNoPersonalValues(entry.Data, before);
        ArquebusierRegistrationTests.AssertNoPersonalValues(entry.Data, after);
        foreach (var value in new[] { "MALE", "FEMALE", "RESERVE", "A_PROF", "2024-12-02", "2025-06-18", "2025-11-15", RegistryTestHost.Iso(RegistryTestHost.DefaultIssuedOn) })
        {
            Assert.DoesNotContain(value, entry.Data!, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("ArquebusierRegistered")]
    [InlineData("ArquebusierUpdated")]
    [InlineData("ArquebusierTransferred")]
    [InlineData("ArquebusierDeleted")]
    [InlineData("OwnedWeaponAdded")]
    [InlineData("OwnedWeaponUpdated")]
    [InlineData("OwnedWeaponRemoved")]
    public async Task A_change_whose_audit_entry_fails_is_not_saved(string action)
    {
        await using var registry = await RegistryTestHost.StartAsync(postgres, mailpit, services => FailingAuditTrail.Decorate(services, action));
        var existing = RegistryData.NewArquebusier(registry.Own.Id);
        var model = RegistryData.NewWeaponModel("ARCABUZ SINTÉTICO AUDITADO");
        await registry.Services.SaveCatalogAsync(model);
        await registry.Services.SaveRegistryAsync(existing);
        var weapon = RegistryData.NewOwnedWeapon(existing.Id, model.Id, "SINT-AUDIT");
        await registry.Services.SaveRegistryAsync(weapon);
        var current = await ReadAsync<JsonElement>(await registry.Admin.GetAsync($"/api/arquebusiers/{existing.Id}", TestContext.Current.CancellationToken));
        var before = await SnapshotAsync(registry);
        var ct = TestContext.Current.CancellationToken;

        using var response = action switch
        {
            "ArquebusierRegistered" => await registry.Admin.PostAsync("/api/arquebusiers", RegistryTestHost.NewArquebusier(registry.Own.Id)),
            "ArquebusierUpdated" => await registry.Admin.PutAsJsonAsync($"/api/arquebusiers/{existing.Id}", WithPhone(current), ct),
            "ArquebusierTransferred" => await registry.Admin.PostAsJsonAsync($"/api/arquebusiers/{existing.Id}/transfer", new { comparsaId = registry.Other.Id }, ct),
            "ArquebusierDeleted" => await registry.Admin.DeleteAsync($"/api/arquebusiers/{existing.Id}", ct),
            "OwnedWeaponAdded" => await registry.Admin.PostAsJsonAsync(
                $"/api/arquebusiers/{existing.Id}/owned-weapons", new { weaponModelId = model.Id, weaponNumber = "2", ownershipGuideNumber = "SINT-AUDIT-2" }, ct),
            "OwnedWeaponUpdated" => await registry.Admin.PutAsJsonAsync(
                $"/api/arquebusiers/{existing.Id}/owned-weapons/{weapon.Id}",
                new { weaponModelId = model.Id, weaponNumber = "3", ownershipGuideNumber = "SINT-AUDIT", version = WeaponVersion(current, weapon.Id) },
                ct),
            _ => await registry.Admin.DeleteAsync($"/api/arquebusiers/{existing.Id}/owned-weapons/{weapon.Id}", ct),
        };

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(before, await SnapshotAsync(registry));
    }

    [Fact]
    public async Task Two_edits_of_the_same_version_at_once_save_one()
    {
        await using var registry = await RegistryTestHost.StartAsync(postgres, mailpit);
        var arquebusier = await registry.RegisterAsync(registry.Own.Id);
        var ct = TestContext.Current.CancellationToken;

        var responses = await Task.WhenAll(
            TwoPhones.Select(phone =>
            {
                var body = ArquebusierEditingTests.EditOf(arquebusier);
                body["phone"] = phone;
                return registry.Admin.PutAsJsonAsync($"/api/arquebusiers/{Id(arquebusier)}", body, ct);
            }));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        await AssertProblemAsync(Assert.Single(responses, r => r.StatusCode != HttpStatusCode.OK), HttpStatusCode.Conflict, "arquebusiers.modified");
        Assert.Single(await registry.Host.AuditEntriesAsync("ArquebusierUpdated"));
    }

    [Fact]
    public async Task Two_edits_of_the_same_weapon_version_at_once_save_one()
    {
        BarrierAuditTrail barrier = null!;
        await using var registry = await RegistryTestHost.StartAsync(
            postgres, mailpit, services => barrier = BarrierAuditTrail.Decorate(services, "OwnedWeaponUpdated", parties: 2));
        var model = RegistryData.NewWeaponModel("ARCABUZ SINTÉTICO DOBLE");
        await registry.Services.SaveCatalogAsync(model);
        var owner = RegistryData.NewArquebusier(registry.Own.Id);
        await registry.Services.SaveRegistryAsync(owner);
        var weapon = RegistryData.NewOwnedWeapon(owner.Id, model.Id, "SINT-DOBLE");
        await registry.Services.SaveRegistryAsync(weapon);
        var current = await ReadAsync<JsonElement>(await registry.Admin.GetAsync($"/api/arquebusiers/{owner.Id}", TestContext.Current.CancellationToken));
        var version = WeaponVersion(current, weapon.Id);

        var responses = await Task.WhenAll(TwoWeaponNumbers.Select(number => registry.Admin.PutAsJsonAsync(
            $"/api/arquebusiers/{owner.Id}/owned-weapons/{weapon.Id}",
            new { weaponModelId = model.Id, weaponNumber = number, ownershipGuideNumber = "SINT-DOBLE", version },
            TestContext.Current.CancellationToken)));

        Assert.Equal(2, barrier.Arrived);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        await AssertProblemAsync(Assert.Single(responses, r => r.StatusCode != HttpStatusCode.OK), HttpStatusCode.Conflict, "ownedWeapons.modified");
    }

    [Fact]
    public async Task An_edit_racing_a_deletion_is_saved_first_or_not_found_never_an_error()
    {
        await using var registry = await RegistryTestHost.StartAsync(postgres, mailpit);
        var arquebusier = await registry.RegisterAsync(registry.Own.Id);
        var ct = TestContext.Current.CancellationToken;

        var edit = registry.FiringChief.PutAsJsonAsync($"/api/arquebusiers/{Id(arquebusier)}", WithPhone(arquebusier), ct);
        var deletion = registry.Admin.DeleteAsync($"/api/arquebusiers/{Id(arquebusier)}", ct);
        await Task.WhenAll(edit, deletion);

        Assert.Equal(HttpStatusCode.NoContent, deletion.Result.StatusCode);
        Assert.True(
            edit.Result.StatusCode is HttpStatusCode.OK or HttpStatusCode.NotFound,
            $"The edit answered {(int)edit.Result.StatusCode}.");
    }

    [Theory]
    [InlineData("PUT", "")]
    [InlineData("DELETE", "")]
    [InlineData("POST", "/transfer")]
    [InlineData("POST", "/owned-weapons")]
    [InlineData("PUT", "/owned-weapons/{weaponId}")]
    [InlineData("DELETE", "/owned-weapons/{weaponId}")]
    public async Task Every_write_on_a_row_held_past_the_lock_timeout_answers_a_retryable_503(string method, string path)
    {
        await using var registry = await RegistryTestHost.StartAsync(postgres, mailpit);
        var ct = TestContext.Current.CancellationToken;
        var model = RegistryData.NewWeaponModel("ARCABUZ SINTÉTICO OCUPADO");
        await registry.Services.SaveCatalogAsync(model);
        var owner = RegistryData.NewArquebusier(registry.Own.Id);
        await registry.Services.SaveRegistryAsync(owner);
        var weapon = RegistryData.NewOwnedWeapon(owner.Id, model.Id, "SINT-OCUPADA");
        await registry.Services.SaveRegistryAsync(weapon);
        var current = await ReadAsync<JsonElement>(await registry.Admin.GetAsync($"/api/arquebusiers/{owner.Id}", ct));

        await using var scope = registry.Services.CreateAsyncScope();
        await using var holder = await scope.ServiceProvider.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        await using var transaction = await holder.BeginTransactionAsync(ct);
        await using (var hold = new NpgsqlCommand($"SELECT 1 FROM registry.arquebusiers WHERE id = '{owner.Id}' FOR UPDATE", holder, transaction))
        {
            await hold.ExecuteScalarAsync(ct);
        }

        using var request = new HttpRequestMessage(new HttpMethod(method), $"/api/arquebusiers/{owner.Id}{path.Replace("{weaponId}", weapon.Id.ToString(), StringComparison.Ordinal)}");
        request.Content = (method, path) switch
        {
            ("PUT", "") => JsonContent.Create(WithPhone(current)),
            ("POST", "/transfer") => JsonContent.Create(new { comparsaId = registry.Other.Id }),
            ("POST", _) => JsonContent.Create(new { weaponModelId = model.Id, weaponNumber = "5", ownershipGuideNumber = "SINT-OCUPADA-2" }),
            ("PUT", _) => JsonContent.Create(new { weaponModelId = model.Id, weaponNumber = "6", ownershipGuideNumber = "SINT-OCUPADA", version = WeaponVersion(current, weapon.Id) }),
            _ => null,
        };
        using var response = await registry.Admin.SendAsync(request, ct);

        await AssertProblemAsync(response, HttpStatusCode.ServiceUnavailable, "registry.busy");
    }

    [Theory]
    [InlineData("/api/arquebusiers", "phoneNumber")]
    [InlineData("/api/arquebusiers", "license.expiryDate")]
    [InlineData("/owned-weapons", "guideNumber")]
    public async Task Unknown_members_are_refused_when_creating_so_a_typo_never_drops_data(string route, string member)
    {
        await using var registry = await RegistryTestHost.StartAsync(postgres, mailpit);
        var ct = TestContext.Current.CancellationToken;
        Dictionary<string, object?> body;
        string path;
        if (route == "/owned-weapons")
        {
            var model = RegistryData.NewWeaponModel("ARCABUZ SINTÉTICO ERRATA");
            await registry.Services.SaveCatalogAsync(model);
            var owner = await registry.RegisterAsync(registry.Own.Id);
            path = $"/api/arquebusiers/{Id(owner)}/owned-weapons";
            body = new() { ["weaponModelId"] = model.Id, ["weaponNumber"] = "7", ["ownershipGuideNumber"] = "SINT-ERRATA" };
        }
        else
        {
            path = route;
            body = RegistryTestHost.NewArquebusier(registry.Own.Id);
        }

        if (member.StartsWith("license.", StringComparison.Ordinal))
        {
            ((Dictionary<string, object?>)body["license"]!)[member["license.".Length..]] = "2030-01-01";
        }
        else
        {
            body[member] = "SINT-0000";
        }

        var before = await SnapshotAsync(registry);
        using var response = await registry.Admin.PostAsJsonAsync(path, body, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await SnapshotAsync(registry));
    }

    private static string Id(JsonElement arquebusier) => arquebusier.GetProperty("id").GetString()!;

    private static Dictionary<string, object?> WithPhone(JsonElement arquebusier)
    {
        var body = ArquebusierEditingTests.EditOf(arquebusier);
        body["phone"] = "+34 600 000 0" + Random.Shared.Next(50, 99).ToString(System.Globalization.CultureInfo.InvariantCulture);
        return body;
    }

    private static uint WeaponVersion(JsonElement arquebusier, Guid weaponId) =>
        arquebusier.GetProperty("ownedWeapons").EnumerateArray()
            .Single(weapon => weapon.GetProperty("id").GetGuid() == weaponId)
            .GetProperty("version").GetUInt32();

    /// <summary>Every registry row as text, with its version, to compare before and after a request.</summary>
    private static async Task<string> SnapshotAsync(RegistryTestHost registry)
    {
        await using var scope = registry.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PolvorApp.ArquebusierRegistry.Persistence.ArquebusierRegistryDbContext>();
        var arquebusiers = await db.Arquebusiers.AsNoTracking().OrderBy(a => a.Id)
            .Select(a => $"{a.Id}|{a.ComparsaId}|{a.Version}").ToListAsync(TestContext.Current.CancellationToken);
        var weapons = await db.OwnedWeapons.AsNoTracking().OrderBy(w => w.Id)
            .Select(w => $"{w.Id}|{w.ArquebusierId}|{w.Version}").ToListAsync(TestContext.Current.CancellationToken);
        return string.Join('\n', arquebusiers.Concat(weapons));
    }

    /// <summary>Throws when <c>action</c> is recorded: the change it belongs to must then roll back.</summary>
    private sealed class FailingAuditTrail(IAuditTrail inner, string action) : IAuditTrail
    {
        public static void Decorate(IServiceCollection services, string action)
        {
            var original = services.Last(d => d.ServiceType == typeof(IAuditTrail));
            services.Remove(original);
            services.AddScoped<IAuditTrail>(provider =>
                new FailingAuditTrail((IAuditTrail)ActivatorUtilities.CreateInstance(provider, original.ImplementationType!), action));
        }

        public void Record(DbContext context, AuditRecord record)
        {
            if (record.Action == action)
            {
                throw new InvalidOperationException("Synthetic audit failure.");
            }

            inner.Record(context, record);
        }
    }
}
