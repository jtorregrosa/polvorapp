using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.ComparsaOrders.Entries;
using PolvorApp.ComparsaOrders.Orders;
using PolvorApp.Distribution.Persistence;
using PolvorApp.FestivalEditions.Editions;

namespace PolvorApp.Api.Tests.Infrastructure;

/// <summary>Synthetic distribution days, slots and proxies written directly, for tests that need state the API reaches in several steps.</summary>
public static class DistributionData
{
    /// <summary>A license valid through the whole synthetic 2031 festival.</summary>
    public static readonly DateOnly ValidThrough = new(2033, 12, 31);

    /// <summary>Saves days, slots and proxies in the distribution schema.</summary>
    internal static async Task SaveDistributionAsync(this IServiceProvider services, params object[] entities)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DistributionDbContext>();
        db.AddRange(entities);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// An entry of <paramref name="order"/> for a new synthetic arquebusier of the order's comparsa with an
    /// <c>AE</c> license expiring on <paramref name="licenseExpiresOn"/> (null: no license; pending when
    /// <paramref name="pending"/>), with <paramref name="powderKg"/> and <paramref name="weaponSource"/>.
    /// </summary>
    internal static async Task<EditionEntry> AddLicensedEntryAsync(
        this OrderTestHost host,
        ComparsaOrder order,
        string lastName,
        DateOnly? licenseExpiresOn,
        int powderKg = 0,
        WeaponSource weaponSource = WeaponSource.None,
        ArquebusierStatus status = ArquebusierStatus.Active,
        bool pending = false,
        FlaskOption flask = FlaskOption.None)
    {
        var arquebusier = RegistryData.NewArquebusier(order.ComparsaId, lastName);
        if (pending)
        {
            (arquebusier.LicenseType, arquebusier.LicensePending) = (LicenseType.Ae, true);
        }
        else if (licenseExpiresOn is { } expires)
        {
            (arquebusier.LicenseType, arquebusier.LicenseIssuedOn, arquebusier.LicenseExpiresOn) = (LicenseType.Ae, expires.AddYears(-5), expires);
        }

        await host.Services.SaveRegistryAsync([arquebusier]);
        var entry = OrderData.NewEntry(order, arquebusier.Id, status);
        (entry.FirstName, entry.LastName, entry.NationalId, entry.FederationId) = (arquebusier.FirstName, arquebusier.LastName, arquebusier.NationalId, arquebusier.FederationId);
        if (status == ArquebusierStatus.Active)
        {
            entry.PowderKg = powderKg;
            entry.WeaponSource = weaponSource;
            entry.RentalWeaponModelId = weaponSource == WeaponSource.Rental ? host.Offered.Id : null;
            entry.Flask = flask;
        }

        await host.Services.SaveOrdersAsync(entry);
        return entry;
    }

    /// <summary>An order of <paramref name="comparsaId"/> in <paramref name="edition"/>, saved.</summary>
    internal static async Task<ComparsaOrder> AddOrderAsync(this OrderTestHost host, FestivalEdition edition, Guid comparsaId, OrderStatus status = OrderStatus.Validated)
    {
        var order = OrderData.NewOrder(edition, comparsaId, status);
        await host.Services.SaveOrdersAsync(order);
        return order;
    }

    public static Task<HttpResponseMessage> RegisterProxyAsync(this HttpClient client, Guid editionId, Guid holderEntryId, Guid proxyEntryId, string type = "POWDER") =>
        client.PostAsJsonAsync($"/api/distribution/editions/{editionId}/proxies", new { holderEntryId, proxyEntryId, type }, TestContext.Current.CancellationToken);

    public static Task<HttpResponseMessage> RemoveProxyAsync(this HttpClient client, Guid proxyId) =>
        client.DeleteAsync($"/api/distribution/proxies/{proxyId}", TestContext.Current.CancellationToken);
}
