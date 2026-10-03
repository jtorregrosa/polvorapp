using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.ArquebusierRegistry.Arquebusiers;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ArquebusierRegistry.OwnedWeapons;
using PolvorApp.ComparsaOrders.Persistence;
using PolvorApp.FederationCatalog.Comparsas;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.FederationCatalog.WeaponModels;
using PolvorApp.FestivalEditions.Contracts;
using PolvorApp.FestivalEditions.Editions;
using PolvorApp.FestivalEditions.Persistence;

namespace PolvorApp.Api.Tests.Infrastructure;

/// <summary>
/// The registry host (an Admin, a FiringChief of <see cref="Own"/>, <see cref="Other"/> outside their
/// scope and <see cref="Inactive"/>) plus a closed 2030 edition, the 2031 edition in progress with open
/// orders, a rentable model offered in 2031 and one that is not. All data is synthetic.
/// </summary>
public sealed class OrderTestHost : IAsyncDisposable
{
    private int _guide;

    private OrderTestHost(RegistryTestHost registry) => Registry = registry;

    public RegistryTestHost Registry { get; }

    public HttpClient Admin => Registry.Admin;

    public HttpClient FiringChief => Registry.FiringChief;

    public IServiceProvider Services => Registry.Services;

    public IdentityTestHost Host => Registry.Host;

    internal Comparsa Own => Registry.Own;

    internal Comparsa Other => Registry.Other;

    internal Comparsa Inactive => Registry.Inactive;

    internal FestivalEdition Previous { get; } = OrderData.NewEdition(2030, EditionStatus.Closed);

    internal FestivalEdition Current { get; } = OrderData.NewEdition(2031, EditionStatus.InProgress, ordersOpen: true);

    internal WeaponModel Offered { get; } = Rentable(RegistryData.NewWeaponModel("ARCABUZ SINTÉTICO OFRECIDO"));

    internal WeaponModel NotOffered { get; } = Rentable(RegistryData.NewWeaponModel("ARCABUZ SINTÉTICO NO OFRECIDO"));

    public static async Task<OrderTestHost> StartAsync(
        PostgresFixture postgres,
        MailpitFixture mailpit,
        Action<IServiceCollection>? configureServices = null,
        IReadOnlyDictionary<string, string?>? settings = null)
    {
        var host = new OrderTestHost(await RegistryTestHost.StartAsync(postgres, mailpit, configureServices, settings));
        host.NotOffered.Handedness = Handedness.Left;
        await host.Services.SaveCatalogAsync(host.Offered, host.NotOffered);
        await host.Services.SaveEditionsAsync(
            host.Previous,
            host.Current,
            new EditionWeaponModel { EditionId = host.Previous.Id, WeaponModelId = host.Offered.Id },
            new EditionWeaponModel { EditionId = host.Current.Id, WeaponModelId = host.Offered.Id });
        return host;
    }

    /// <summary>Registers an arquebusier of <paramref name="comparsaId"/> directly, with <paramref name="weapons"/> owned weapons of the offered model.</summary>
    internal async Task<(Arquebusier Arquebusier, List<OwnedWeapon> Weapons)> AddArquebusierAsync(
        Guid comparsaId, string lastName, ArquebusierStatus status = ArquebusierStatus.Active, int weapons = 0)
    {
        var arquebusier = RegistryData.NewArquebusier(comparsaId, lastName);
        arquebusier.Status = status;
        var owned = Enumerable.Range(0, weapons)
            .Select(_ => RegistryData.NewOwnedWeapon(arquebusier.Id, Offered.Id, $"GUIA-PEDIDO-{Interlocked.Increment(ref _guide)}"))
            .ToList();
        await Services.SaveRegistryAsync([arquebusier, .. owned]);
        return (arquebusier, owned);
    }

    /// <summary>Opens or closes the orders of the current edition directly.</summary>
    public async Task SetOrdersOpenAsync(bool open)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FestivalEditionsDbContext>();
        await db.Editions.Where(e => e.Id == Current.Id).ExecuteUpdateAsync(e => e.SetProperty(x => x.OrdersOpen, open), TestContext.Current.CancellationToken);
    }

    /// <summary>Moves the current edition to another status directly (orders closed).</summary>
    internal async Task SetCurrentStatusAsync(EditionStatus status)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FestivalEditionsDbContext>();
        await db.Editions.Where(e => e.Id == Current.Id)
            .ExecuteUpdateAsync(e => e.SetProperty(x => x.Status, status).SetProperty(x => x.OrdersOpen, false), TestContext.Current.CancellationToken);
    }

    /// <summary>Prepares the order of <paramref name="comparsaId"/> in the current edition and returns its body.</summary>
    public async Task<JsonElement> PrepareAsync(HttpClient client, Guid comparsaId)
    {
        using var response = await client.PostAsync("/api/comparsa-orders", new { editionId = Current.Id, comparsaId });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await IdentityAssertions.ReadAsync<JsonElement>(response);
    }

    /// <summary>The order as <paramref name="client"/> reads it.</summary>
    public static async Task<JsonElement> GetOrderAsync(HttpClient client, Guid orderId)
    {
        using var response = await client.GetAsync($"/api/comparsa-orders/{orderId}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await IdentityAssertions.ReadAsync<JsonElement>(response);
    }

    /// <summary>Reads the orders schema directly.</summary>
    internal async Task<T> ReadOrdersAsync<T>(Func<ComparsaOrdersDbContext, Task<T>> read)
    {
        await using var scope = Services.CreateAsyncScope();
        return await read(scope.ServiceProvider.GetRequiredService<ComparsaOrdersDbContext>());
    }

    public ValueTask DisposeAsync() => Registry.DisposeAsync();

    private static WeaponModel Rentable(WeaponModel model)
    {
        model.Rentable = true;
        return model;
    }
}
