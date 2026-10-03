using System.Net;
using System.Text.Json;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ComparsaOrders.Entries;
using PolvorApp.ComparsaOrders.Orders;
using static PolvorApp.Api.Tests.Infrastructure.DistributionData;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Distribution;

/// <summary>
/// Spec "Pickup proxies follow their entries (BR-14)" (design D2): an arquebusier deleted from the registry
/// while the orders are open takes their entry, and with it every proxy the entry is in; with the orders
/// closed the entry stays as history and so does the proxy, which then no longer holds.
/// </summary>
public sealed class ProxyRegistryDeletionTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private OrderTestHost _orders = null!;
    private ComparsaOrder _order = null!;
    private EditionEntry _holder = null!;
    private EditionEntry _proxy = null!;

    public async ValueTask InitializeAsync()
    {
        _orders = await OrderTestHost.StartAsync(postgres, mailpit);
        _order = await _orders.AddOrderAsync(_orders.Current, _orders.Own.Id, PolvorApp.ComparsaOrders.Contracts.OrderStatus.Draft);
        _holder = await _orders.AddLicensedEntryAsync(_order, "Abad Sintética", ValidThrough, powderKg: 2);
        _proxy = await _orders.AddLicensedEntryAsync(_order, "Zamora Sintético", ValidThrough, status: ArquebusierStatus.Reserve);
        using var response = await _orders.FiringChief.RegisterProxyAsync(_orders.Current.Id, _holder.Id, _proxy.Id);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    public async ValueTask DisposeAsync() => await _orders.DisposeAsync();

    [Fact]
    public async Task Deleting_the_proxys_arquebusier_with_the_orders_open_removes_the_proxy()
    {
        await DeleteAsync(_proxy.ArquebusierId!.Value);

        Assert.Empty(await ListAsync());
        Assert.Equal(0, await DistributionRequests.CountProxiesAsync(_orders.Services));
    }

    [Fact]
    public async Task Deleting_the_holders_arquebusier_with_the_orders_open_removes_the_proxy()
    {
        await DeleteAsync(_holder.ArquebusierId!.Value);

        Assert.Equal(0, await DistributionRequests.CountProxiesAsync(_orders.Services));
    }

    [Fact]
    public async Task With_the_orders_closed_the_proxy_stays_and_no_longer_holds()
    {
        await _orders.SetOrdersOpenAsync(false);

        await DeleteAsync(_proxy.ArquebusierId!.Value);

        var proxy = Assert.Single(await ListAsync());
        Assert.Equal("LICENSE_INVALID", proxy.GetProperty("problem").GetString());
    }

    private async Task DeleteAsync(Guid arquebusierId)
    {
        using var response = await _orders.Admin.DeleteAsync($"/api/arquebusiers/{arquebusierId}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private async Task<List<JsonElement>> ListAsync()
    {
        using var response = await _orders.Admin.GetAsync($"/api/distribution/editions/{_orders.Current.Id}/proxies", TestContext.Current.CancellationToken);
        return [.. (await ReadAsync<JsonElement>(response)).EnumerateArray()];
    }
}
