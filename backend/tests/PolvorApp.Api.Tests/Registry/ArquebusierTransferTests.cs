using System.Net;
using System.Text.Json;
using PolvorApp.Api.Tests.Infrastructure;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Registry;

/// <summary>Spec "Transfer between comparsas (UC-29, BR-13)" and its audit entry.</summary>
[Collection(PostgresGroup.Name)]
public sealed class ArquebusierTransferTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private RegistryTestHost _registry = null!;

    public async ValueTask InitializeAsync() => _registry = await RegistryTestHost.StartAsync(postgres, mailpit);

    public async ValueTask DisposeAsync() => await _registry.DisposeAsync();

    [Fact]
    public async Task An_Admin_moves_an_arquebusier_and_their_owned_weapons()
    {
        var model = RegistryData.NewWeaponModel("ARCABUZ SINTÉTICO VIAJERO");
        await _registry.Services.SaveCatalogAsync(model);
        var arquebusier = await _registry.RegisterAsync(_registry.Own.Id);
        var id = arquebusier.GetProperty("id").GetGuid();
        await _registry.Services.SaveRegistryAsync(RegistryData.NewOwnedWeapon(id, model.Id, "SINT-2001"));

        using var response = await TransferAsync(_registry.Admin, id, _registry.Other.Id);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var moved = await ReadAsync<JsonElement>(await _registry.Admin.GetAsync($"/api/arquebusiers/{id}", TestContext.Current.CancellationToken));
        Assert.Equal(_registry.Other.Id, moved.GetProperty("comparsaId").GetGuid());
        Assert.Single(moved.GetProperty("ownedWeapons").EnumerateArray());
        using var lost = await _registry.FiringChief.GetAsync($"/api/arquebusiers/{id}", TestContext.Current.CancellationToken);
        await AssertProblemAsync(lost, HttpStatusCode.NotFound, "arquebusiers.notFound");
    }

    [Fact]
    public async Task The_FiringChiefs_of_the_target_gain_the_arquebusier()
    {
        var arquebusier = await _registry.RegisterAsync(_registry.Other.Id);
        var id = arquebusier.GetProperty("id").GetGuid();

        using var response = await TransferAsync(_registry.Admin, id, _registry.Own.Id);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using var gained = await _registry.FiringChief.GetAsync($"/api/arquebusiers/{id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, gained.StatusCode);
    }

    [Fact]
    public async Task Invalid_targets_are_blocking()
    {
        var id = (await _registry.RegisterAsync(_registry.Own.Id)).GetProperty("id").GetGuid();

        using var inactive = await TransferAsync(_registry.Admin, id, _registry.Inactive.Id);
        using var same = await TransferAsync(_registry.Admin, id, _registry.Own.Id);
        using var unknown = await TransferAsync(_registry.Admin, id, Guid.CreateVersion7());
        using var nobody = await TransferAsync(_registry.Admin, Guid.CreateVersion7(), _registry.Other.Id);
        using var missing = await _registry.Admin.PostAsync($"/api/arquebusiers/{id}/transfer", new { });

        await AssertProblemAsync(inactive, HttpStatusCode.Conflict, "arquebusiers.comparsaInactive");
        await AssertProblemAsync(same, HttpStatusCode.Conflict, "arquebusiers.sameComparsa");
        await AssertProblemAsync(unknown, HttpStatusCode.NotFound, "arquebusiers.comparsaNotFound");
        await AssertProblemAsync(nobody, HttpStatusCode.NotFound, "arquebusiers.notFound");
        Assert.Equal("required", (await ErrorsAsync(missing))["comparsaId"]);
        Assert.Empty(await _registry.Host.AuditEntriesAsync("ArquebusierTransferred"));
    }

    [Fact]
    public async Task A_FiringChief_cannot_transfer_even_between_their_own_comparsas()
    {
        var id = (await _registry.RegisterAsync(_registry.Own.Id)).GetProperty("id").GetGuid();
        var second = RegistryData.NewComparsa("Comparsa Sintética Segunda");
        await _registry.Services.SaveCatalogAsync(second);
        await _registry.Host.AssignAsync(second.Id, _registry.FiringChiefId);

        using var response = await TransferAsync(_registry.FiringChief, id, second.Id);
        using var unknownMember = await _registry.Admin.PostAsync($"/api/arquebusiers/{id}/transfer", new { comparsaId = second.Id, reason = "x" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unknownMember.StatusCode);
        var current = await ReadAsync<JsonElement>(await _registry.Admin.GetAsync($"/api/arquebusiers/{id}", TestContext.Current.CancellationToken));
        Assert.Equal(_registry.Own.Id, current.GetProperty("comparsaId").GetGuid());
    }

    [Fact]
    public async Task A_transfer_is_audited_with_both_comparsas()
    {
        var id = (await _registry.RegisterAsync(_registry.Own.Id)).GetProperty("id").GetGuid();

        using var response = await TransferAsync(_registry.Admin, id, _registry.Other.Id);

        var entry = Assert.Single(await _registry.Host.AuditEntriesAsync("ArquebusierTransferred"));
        Assert.Equal((_registry.AdminId, _registry.Own.Id, id.ToString()), (entry.ActorUserId, entry.ComparsaId, entry.EntityId));
        using var data = JsonDocument.Parse(entry.Data!);
        Assert.Equal(
            (_registry.Own.Id, _registry.Other.Id),
            (data.RootElement.GetProperty("fromComparsaId").GetGuid(), data.RootElement.GetProperty("toComparsaId").GetGuid()));
    }

    private static Task<HttpResponseMessage> TransferAsync(HttpClient client, Guid id, Guid comparsaId) =>
        client.PostAsync($"/api/arquebusiers/{id}/transfer", new { comparsaId });
}

/// <summary>Design D10: a transfer racing with the deletion of its target never leaves a dangling reference.</summary>
[Collection(PostgresGroup.Name)]
public sealed class ArquebusierTransferRaceTests(PostgresFixture postgres, MailpitFixture mailpit)
{
    [Fact]
    public async Task A_target_deleted_during_the_transfer_leaves_the_arquebusier_where_it_was()
    {
        DeletingCatalogDirectory deleting = null!;
        await using var registry = await RegistryTestHost.StartAsync(postgres, mailpit, services => deleting = DeletingCatalogDirectory.Decorate(services));
        var target = RegistryData.NewComparsa("Comparsa Sintética Efímera");
        await registry.Services.SaveCatalogAsync(target);
        var id = (await registry.RegisterAsync(registry.Own.Id)).GetProperty("id").GetGuid();
        deleting.Target = target.Id;

        using var response = await registry.Admin.PostAsync($"/api/arquebusiers/{id}/transfer", new { comparsaId = target.Id });

        Assert.True(deleting.Deleted);
        await AssertProblemAsync(response, HttpStatusCode.NotFound, "arquebusiers.comparsaNotFound");
        var current = await ReadAsync<JsonElement>(await registry.Admin.GetAsync($"/api/arquebusiers/{id}", TestContext.Current.CancellationToken));
        Assert.Equal(registry.Own.Id, current.GetProperty("comparsaId").GetGuid());
    }
}
