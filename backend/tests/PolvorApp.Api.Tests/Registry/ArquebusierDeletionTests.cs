using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.AuditPrivacy.Persistence;
using PolvorApp.FederationCatalog.WeaponModels;
using PolvorApp.SharedKernel.Auditing;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Registry;

/// <summary>Spec "Deleting an arquebusier (UC-05, BR-14)" and its audit entry.</summary>
public sealed class ArquebusierDeletionTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private readonly WeaponModel _model = RegistryData.NewWeaponModel("ARCABUZ SINTÉTICO BORRABLE");
    private RegistryTestHost _registry = null!;

    public async ValueTask InitializeAsync()
    {
        _registry = await RegistryTestHost.StartAsync(postgres, mailpit);
        await _registry.Services.SaveCatalogAsync(_model);
    }

    public async ValueTask DisposeAsync() => await _registry.DisposeAsync();

    [Fact]
    public async Task A_FiringChief_deletes_an_arquebusier_with_their_owned_weapons()
    {
        var arquebusier = await _registry.RegisterAsync(_registry.Own.Id);
        var id = Id(arquebusier);
        await AddWeaponAsync(id, "SINT-3001");
        await AddWeaponAsync(id, "SINT-3002");

        using var response = await _registry.FiringChief.DeleteAsync($"/api/arquebusiers/{id}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using var gone = await _registry.Admin.GetAsync($"/api/arquebusiers/{id}", TestContext.Current.CancellationToken);
        await AssertProblemAsync(gone, HttpStatusCode.NotFound, "arquebusiers.notFound");
        await using var scope = _registry.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PolvorApp.ArquebusierRegistry.Persistence.ArquebusierRegistryDbContext>();
        Assert.False(await db.OwnedWeapons.AnyAsync(w => w.ArquebusierId == Guid.Parse(id), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task An_Admin_deletes_in_any_comparsa_and_the_identifiers_can_be_registered_again()
    {
        var arquebusier = await _registry.RegisterAsync(_registry.Other.Id);
        var id = Id(arquebusier);
        await AddWeaponAsync(id, "SINT-3003");

        using var deleted = await _registry.Admin.DeleteAsync($"/api/arquebusiers/{id}", TestContext.Current.CancellationToken);
        var again = await _registry.RegisterAsync(_registry.Own.Id, body =>
        {
            body["nationalId"] = arquebusier.GetProperty("nationalId").GetString();
            body["federationId"] = arquebusier.GetProperty("federationId").GetInt32();
        });
        await AddWeaponAsync(Id(again), "SINT-3003");

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.NotEqual(id, Id(again));
    }

    [Fact]
    public async Task An_arquebusier_outside_the_scope_cannot_be_deleted()
    {
        var id = Id(await _registry.RegisterAsync(_registry.Other.Id));

        using var response = await _registry.FiringChief.DeleteAsync($"/api/arquebusiers/{id}", TestContext.Current.CancellationToken);

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "arquebusiers.notFound");
        Assert.Equal(HttpStatusCode.OK, (await _registry.Admin.GetAsync($"/api/arquebusiers/{id}", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Empty(await _registry.Host.AuditEntriesAsync("ArquebusierDeleted"));
    }

    [Fact]
    public async Task No_audit_entry_keeps_personal_data_of_a_deleted_arquebusier()
    {
        var arquebusier = await _registry.RegisterAsync(_registry.Own.Id);
        var id = Id(arquebusier);
        var edit = ArquebusierEditingTests.EditOf(arquebusier);
        edit["phone"] = "+34 688 000 010";
        using (var edited = await _registry.FiringChief.PutAsJsonAsync($"/api/arquebusiers/{id}", edit, TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        }

        await AddWeaponAsync(id, "SINT-3004");

        using var response = await _registry.FiringChief.DeleteAsync($"/api/arquebusiers/{id}", TestContext.Current.CancellationToken);

        var entry = Assert.Single(await _registry.Host.AuditEntriesAsync("ArquebusierDeleted"));
        Assert.Equal((_registry.FiringChiefId, _registry.Own.Id, id), (entry.ActorUserId, entry.ComparsaId, entry.EntityId));
        using (var data = JsonDocument.Parse(entry.Data!))
        {
            Assert.Equal(1, data.RootElement.GetProperty("ownedWeaponCount").GetInt32());
        }

        foreach (var any in await EntriesAboutAsync(id))
        {
            ArquebusierRegistrationTests.AssertNoPersonalValues(any.Data, arquebusier);
            Assert.DoesNotContain("SINT-3004", any.Data ?? string.Empty, StringComparison.Ordinal);
            Assert.DoesNotContain("+34 688 000 010", any.Data ?? string.Empty, StringComparison.Ordinal);
        }
    }

    private async Task<List<AuditEntry>> EntriesAboutAsync(string arquebusierId)
    {
        await using var scope = _registry.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        var entries = await db.Set<AuditEntry>().AsNoTracking().ToListAsync(TestContext.Current.CancellationToken);
        var about = entries.Where(e => e.EntityId == arquebusierId || (e.Data?.Contains(arquebusierId, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();
        Assert.True(about.Count >= 4, "registration, edit, weapon and deletion entries");
        return about;
    }

    private async Task AddWeaponAsync(string arquebusierId, string guide)
    {
        using var response = await _registry.Admin.PostAsJsonAsync(
            $"/api/arquebusiers/{arquebusierId}/owned-weapons",
            new { weaponModelId = _model.Id, weaponNumber = "1", ownershipGuideNumber = guide },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static string Id(JsonElement arquebusier) => arquebusier.GetProperty("id").GetString()!;
}
