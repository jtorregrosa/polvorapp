using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.ArquebusierRegistry;
using PolvorApp.ArquebusierRegistry.Persistence;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.IdentityAccess.Contracts;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Registry;

/// <summary>
/// Spec "Comparsas and weapon models in use" (design D3): the registry vetoes deleting what it
/// references, through the catalog's usage contract and, as a backstop, the cross-schema foreign keys.
/// </summary>
[Collection(PostgresGroup.Name)]
public sealed class RegistryCatalogUsageTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private IdentityTestHost _host = null!;
    private HttpClient _admin = null!;

    public async ValueTask InitializeAsync()
    {
        _host = await IdentityTestHost.StartAsync(postgres, mailpit);
        _admin = await _host.SignInAsync(await _host.CreateUserAsync("admin.uso@example.test", UserRole.Admin));
    }

    public async ValueTask DisposeAsync()
    {
        _admin.Dispose();
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task A_comparsa_with_an_arquebusier_cannot_be_deleted()
    {
        var comparsa = RegistryData.NewComparsa("Comparsa Sintética Ocupada");
        await _host.Services.SaveCatalogAsync(comparsa);
        await _host.Services.SaveRegistryAsync(RegistryData.NewArquebusier(comparsa.Id));

        using var response = await _admin.DeleteAsync($"/api/comparsas/{comparsa.Id}", TestContext.Current.CancellationToken);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "comparsas.inUse");
    }

    [Fact]
    public async Task A_weapon_model_with_an_owned_weapon_cannot_be_deleted()
    {
        var comparsa = RegistryData.NewComparsa("Comparsa Sintética Armada");
        var model = RegistryData.NewWeaponModel("ARCABUZ SINTÉTICO EN USO");
        await _host.Services.SaveCatalogAsync(comparsa, model);
        var owner = RegistryData.NewArquebusier(comparsa.Id);
        await _host.Services.SaveRegistryAsync(owner, RegistryData.NewOwnedWeapon(owner.Id, model.Id, "SINT-0300"));

        using var response = await _admin.DeleteAsync($"/api/weapon-models/{model.Id}", TestContext.Current.CancellationToken);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "weaponModels.inUse");
    }

    [Fact]
    public async Task The_registry_reports_its_uses_through_the_catalog_contract()
    {
        var used = RegistryData.NewComparsa("Comparsa Sintética Usada");
        var unused = RegistryData.NewComparsa("Comparsa Sintética Libre");
        var usedModel = RegistryData.NewWeaponModel("ARCABUZ SINTÉTICO USADO");
        var unusedModel = RegistryData.NewWeaponModel("PISTOLA SINTÉTICA LIBRE", WeaponKind.Pistol);
        await _host.Services.SaveCatalogAsync(used, unused, usedModel, unusedModel);
        var owner = RegistryData.NewArquebusier(used.Id);
        await _host.Services.SaveRegistryAsync(owner, RegistryData.NewOwnedWeapon(owner.Id, usedModel.Id, "SINT-0302"));

        await using var scope = _host.Services.CreateAsyncScope();
        var usage = scope.ServiceProvider.GetServices<ICatalogUsage>().OfType<RegistryCatalogUsage>().Single();
        var ct = TestContext.Current.CancellationToken;

        Assert.Equal(
            (true, false, true, false),
            (await usage.IsComparsaInUseAsync(used.Id, ct), await usage.IsComparsaInUseAsync(unused.Id, ct),
             await usage.IsWeaponModelInUseAsync(usedModel.Id, ct), await usage.IsWeaponModelInUseAsync(unusedModel.Id, ct)));
    }

    [Fact]
    public async Task A_comparsa_can_be_deleted_once_its_last_arquebusier_is_moved_away()
    {
        var leaving = RegistryData.NewComparsa("Comparsa Sintética Vaciada");
        var receiving = RegistryData.NewComparsa("Comparsa Sintética Receptora");
        await _host.Services.SaveCatalogAsync(leaving, receiving);
        var arquebusier = RegistryData.NewArquebusier(leaving.Id);
        await _host.Services.SaveRegistryAsync(arquebusier);
        await using (var scope = _host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ArquebusierRegistryDbContext>();
            await db.Arquebusiers.Where(a => a.Id == arquebusier.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(a => a.ComparsaId, receiving.Id), TestContext.Current.CancellationToken);
        }

        using var response = await _admin.DeleteAsync($"/api/comparsas/{leaving.Id}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}

/// <summary>
/// Design D3: with no usage check registered at all, the cross-schema foreign keys alone still
/// keep a referenced comparsa or weapon model from being deleted.
/// </summary>
[Collection(PostgresGroup.Name)]
public sealed class RegistryForeignKeyBackstopTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private IdentityTestHost _host = null!;
    private HttpClient _admin = null!;

    public async ValueTask InitializeAsync()
    {
        _host = await IdentityTestHost.StartAsync(postgres, mailpit, configureServices: services => services.RemoveAll<ICatalogUsage>());
        _admin = await _host.SignInAsync(await _host.CreateUserAsync("admin.respaldo@example.test", UserRole.Admin));
    }

    public async ValueTask DisposeAsync()
    {
        _admin.Dispose();
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task The_foreign_keys_still_block_the_deletion_without_the_usage_check()
    {
        var comparsa = RegistryData.NewComparsa("Comparsa Sintética Sin Comprobación");
        var model = RegistryData.NewWeaponModel("TRABUCO SINTÉTICO SIN COMPROBACIÓN");
        await _host.Services.SaveCatalogAsync(comparsa, model);
        var owner = RegistryData.NewArquebusier(comparsa.Id);
        await _host.Services.SaveRegistryAsync(owner, RegistryData.NewOwnedWeapon(owner.Id, model.Id, "SINT-0301"));

        using var comparsaResponse = await _admin.DeleteAsync($"/api/comparsas/{comparsa.Id}", TestContext.Current.CancellationToken);
        using var modelResponse = await _admin.DeleteAsync($"/api/weapon-models/{model.Id}", TestContext.Current.CancellationToken);

        await AssertProblemAsync(comparsaResponse, HttpStatusCode.Conflict, "comparsas.inUse");
        await AssertProblemAsync(modelResponse, HttpStatusCode.Conflict, "weaponModels.inUse");
    }
}
