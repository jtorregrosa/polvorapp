using System.Net;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.FestivalEditions;
using PolvorApp.IdentityAccess.Contracts;
using static PolvorApp.Api.Tests.Infrastructure.EditionData;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Editions;

/// <summary>
/// Spec "Rental models offered in an edition (BR-07)", scenario "Offered model cannot be deleted from
/// the catalogue": the editions veto it through the catalogue usage contract (design D7), with the
/// cross-schema foreign key as a backstop.
/// </summary>
public sealed class EditionCatalogUsageTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private IdentityTestHost _host = null!;
    private HttpClient _admin = null!;

    public async ValueTask InitializeAsync()
    {
        _host = await IdentityTestHost.StartAsync(postgres, mailpit);
        _admin = await _host.SignInAsync(await _host.CreateUserAsync("admin.uso.ediciones@example.test", UserRole.Admin));
    }

    public async ValueTask DisposeAsync()
    {
        _admin.Dispose();
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task A_model_offered_in_an_edition_cannot_be_deleted_and_the_usage_check_reports_it()
    {
        var (id, _) = await _admin.CreateEditionAsync(2031);
        var model = await _admin.CreateRentableModelAsync("ARCABUZ OFRECIDO EN EDICIÓN");
        await _host.OfferAsync(id, model);

        using var response = await _admin.DeleteAsync($"/api/weapon-models/{model}", TestContext.Current.CancellationToken);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "weaponModels.inUse");
        await using var scope = _host.Services.CreateAsyncScope();
        var usage = Assert.Single(scope.ServiceProvider.GetServices<ICatalogUsage>(), u => u is FestivalEditionsCatalogUsage);
        Assert.True(await usage.IsWeaponModelInUseAsync(model, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task An_unused_model_can_still_be_deleted_and_comparsas_are_never_in_use()
    {
        var model = await _admin.CreateRentableModelAsync("ARCABUZ SIN EDICIÓN");

        using var response = await _admin.DeleteAsync($"/api/weapon-models/{model}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await using var scope = _host.Services.CreateAsyncScope();
        var usage = scope.ServiceProvider.GetServices<ICatalogUsage>().OfType<FestivalEditionsCatalogUsage>().Single();
        Assert.False(await usage.IsComparsaInUseAsync(Guid.CreateVersion7(), TestContext.Current.CancellationToken));
    }
}
