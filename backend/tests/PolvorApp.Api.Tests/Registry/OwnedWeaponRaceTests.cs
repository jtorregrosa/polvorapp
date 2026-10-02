using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.SharedKernel.Security;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Registry;

/// <summary>
/// Design D10 and D11 (group 4 reviews): owned-weapon writes racing each other, the owner's deletion
/// and the catalog end in a clear answer, never a 500; and every registry write is throttled.
/// </summary>
public sealed class OwnedWeaponRaceTests(PostgresFixture postgres, MailpitFixture mailpit)
{
    [Fact]
    public async Task Two_removals_of_the_same_weapon_at_once_remove_it_once()
    {
        BarrierAuditTrail barrier = null!;
        await using var registry = await RegistryTestHost.StartAsync(
            postgres, mailpit, services => barrier = BarrierAuditTrail.Decorate(services, "OwnedWeaponRemoved", parties: 2));
        var model = RegistryData.NewWeaponModel("ARCABUZ SINTÉTICO DOBLE CLIC");
        await registry.Services.SaveCatalogAsync(model);
        var owner = await registry.RegisterAsync(registry.Own.Id);
        var weapon = RegistryData.NewOwnedWeapon(owner.GetProperty("id").GetGuid(), model.Id, "SINT-4001");
        await registry.Services.SaveRegistryAsync(weapon);
        var url = $"/api/arquebusiers/{weapon.ArquebusierId}/owned-weapons/{weapon.Id}";

        var responses = await Task.WhenAll(registry.FiringChief.DeleteAsync(url, TestContext.Current.CancellationToken), registry.Admin.DeleteAsync(url, TestContext.Current.CancellationToken));

        Assert.Equal(2, barrier.Arrived);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.NoContent);
        await AssertProblemAsync(Assert.Single(responses, r => r.StatusCode != HttpStatusCode.NoContent), HttpStatusCode.NotFound, "ownedWeapons.notFound");
        Assert.Single(await registry.Host.AuditEntriesAsync("OwnedWeaponRemoved"));
    }

    [Fact]
    public async Task A_weapon_added_while_its_owner_is_being_deleted_waits_and_finds_no_owner()
    {
        await using var registry = await RegistryTestHost.StartAsync(postgres, mailpit);
        var model = RegistryData.NewWeaponModel("ARCABUZ SINTÉTICO HUÉRFANO");
        await registry.Services.SaveCatalogAsync(model);
        var ownerId = (await registry.RegisterAsync(registry.Own.Id)).GetProperty("id").GetGuid();
        var ct = TestContext.Current.CancellationToken;
        await using var scope = registry.Services.CreateAsyncScope();
        await using var deleter = await scope.ServiceProvider.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        await using var transaction = await deleter.BeginTransactionAsync(ct);
        await using (var delete = new NpgsqlCommand($"DELETE FROM registry.arquebusiers WHERE id = '{ownerId}'", deleter, transaction))
        {
            await delete.ExecuteNonQueryAsync(ct);
        }

        var adding = registry.FiringChief.PostAsJsonAsync(
            $"/api/arquebusiers/{ownerId}/owned-weapons", new { weaponModelId = model.Id, weaponNumber = "1", ownershipGuideNumber = "SINT-4002" }, ct);
        await Task.Delay(TimeSpan.FromMilliseconds(500), ct);
        Assert.False(adding.IsCompleted, "The weapon write should wait for the owner's deletion.");
        await transaction.CommitAsync(ct);

        using var response = await adding;

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "arquebusiers.notFound");
    }

    [Fact]
    public async Task A_model_deleted_between_the_catalog_lookup_and_the_insert_is_a_field_error()
    {
        DeletingCatalogDirectory deleting = null!;
        await using var registry = await RegistryTestHost.StartAsync(postgres, mailpit, services => deleting = DeletingCatalogDirectory.Decorate(services));
        var model = RegistryData.NewWeaponModel("ARCABUZ SINTÉTICO FUGAZ");
        await registry.Services.SaveCatalogAsync(model);
        var ownerId = (await registry.RegisterAsync(registry.Own.Id)).GetProperty("id").GetGuid();
        deleting.ModelTarget = model.Id;

        using var response = await registry.FiringChief.PostAsJsonAsync(
            $"/api/arquebusiers/{ownerId}/owned-weapons",
            new { weaponModelId = model.Id, weaponNumber = "1", ownershipGuideNumber = "SINT-4003" },
            TestContext.Current.CancellationToken);

        Assert.True(deleting.Deleted);
        Assert.Equal("notFound", (await ErrorsAsync(response))["weaponModelId"]);
    }

    [Fact]
    public async Task Every_registry_write_is_throttled_per_user()
    {
        await using var registry = await RegistryTestHost.StartAsync(postgres, mailpit);

        var unthrottled = registry.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.Contains("arquebusiers", StringComparison.Ordinal) == true)
            .Where(e => e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Any(m => m != "GET") == true)
            .Where(e => e.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName != RateLimitPolicies.PersonalDataWrites)
            .Select(e => e.DisplayName)
            .ToList();

        Assert.Empty(unthrottled);
    }
}
