using System.Net;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.Distribution;
using PolvorApp.Distribution.Contracts;
using PolvorApp.Distribution.Days;
using PolvorApp.Distribution.Proxies;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.FestivalEditions.Contracts;
using PolvorApp.IdentityAccess.Contracts;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;
using static PolvorApp.Api.Tests.Infrastructure.OrderData;
using static PolvorApp.Api.Tests.Infrastructure.RegistryData;

namespace PolvorApp.Api.Tests.Distribution;

/// <summary>
/// Design D1: distribution days, slots and proxies are references that block deleting an edition
/// (spec festival-editions: Edition management by Admins) or a comparsa (spec distribution:
/// Distribution slots), reported through the usage contracts.
/// </summary>
public sealed class DistributionUsageTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private IdentityTestHost _host = null!;
    private HttpClient _admin = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _host = await IdentityTestHost.StartAsync(postgres, mailpit);
        _admin = await _host.SignInAsync(await _host.CreateUserAsync("admin.uso.reparto@example.test", UserRole.Admin));
    }

    public async ValueTask DisposeAsync()
    {
        _admin.Dispose();
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task A_draft_edition_with_a_distribution_day_cannot_be_deleted()
    {
        var edition = NewEdition(2031, EditionStatus.Draft);
        await _host.Services.SaveEditionsAsync(edition);
        await _host.Services.SaveDistributionAsync(Day(edition.Id));

        using var response = await _admin.DeleteAsync($"/api/editions/{edition.Id}", Token);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "editions.inUse");
        Assert.True(await EditionUsageAsync(edition.Id));
    }

    [Fact]
    public async Task A_comparsa_with_a_slot_cannot_be_deleted()
    {
        var comparsa = NewComparsa("Comparsa Sintética Con Turno");
        await _host.Services.SaveCatalogAsync(comparsa);
        var edition = NewEdition(2031);
        await _host.Services.SaveEditionsAsync(edition);
        var day = Day(edition.Id);
        day.Slots.Add(new DistributionSlot { DistributionId = day.Id, ComparsaId = comparsa.Id, StartsAt = new TimeOnly(9, 0) });
        await _host.Services.SaveDistributionAsync(day);

        using var response = await _admin.DeleteAsync($"/api/comparsas/{comparsa.Id}", Token);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "comparsas.inUse");
        Assert.True(await CatalogUsageAsync(usage => usage.IsComparsaInUseAsync(comparsa.Id, Token)));
    }

    [Fact]
    public async Task A_comparsa_with_a_proxy_is_in_use()
    {
        var comparsa = NewComparsa("Comparsa Sintética Con Autorizado");
        await _host.Services.SaveCatalogAsync(comparsa);
        var edition = NewEdition(2031);
        await _host.Services.SaveEditionsAsync(edition);
        var order = NewOrder(edition, comparsa.Id);
        var holder = NewEntry(order, null);
        var proxy = NewEntry(order, null);
        await _host.Services.SaveOrdersAsync(order, holder, proxy);
        await _host.Services.SaveDistributionAsync(new PickupProxy
        {
            Id = Guid.CreateVersion7(),
            EditionId = edition.Id,
            ComparsaId = comparsa.Id,
            Type = DistributionType.Powder,
            HolderEntryId = holder.Id,
            ProxyEntryId = proxy.Id,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        Assert.True(await CatalogUsageAsync(usage => usage.IsComparsaInUseAsync(comparsa.Id, Token)));
        Assert.True(await EditionUsageAsync(edition.Id));
    }

    [Fact]
    public async Task Unused_records_and_weapon_models_are_not_in_use()
    {
        var comparsa = NewComparsa("Comparsa Sintética Libre");
        await _host.Services.SaveCatalogAsync(comparsa);
        var edition = NewEdition(2031, EditionStatus.Draft);
        await _host.Services.SaveEditionsAsync(edition);

        Assert.False(await EditionUsageAsync(edition.Id));
        Assert.False(await CatalogUsageAsync(usage => usage.IsComparsaInUseAsync(comparsa.Id, Token)));
        Assert.False(await CatalogUsageAsync(usage => usage.IsWeaponModelInUseAsync(Guid.CreateVersion7(), Token)));
    }

    private static DistributionDay Day(Guid editionId) => new()
    {
        Id = Guid.CreateVersion7(),
        EditionId = editionId,
        Type = DistributionType.Powder,
        Date = new DateOnly(2031, 4, 18),
        Location = "Paraje Sintético",
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    private async Task<bool> EditionUsageAsync(Guid editionId)
    {
        await using var scope = _host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetServices<IEditionUsage>().OfType<DistributionEditionUsage>().Single().IsEditionInUseAsync(editionId, Token);
    }

    private async Task<bool> CatalogUsageAsync(Func<DistributionCatalogUsage, Task<bool>> ask)
    {
        await using var scope = _host.Services.CreateAsyncScope();
        return await ask(scope.ServiceProvider.GetServices<ICatalogUsage>().OfType<DistributionCatalogUsage>().Single());
    }
}
