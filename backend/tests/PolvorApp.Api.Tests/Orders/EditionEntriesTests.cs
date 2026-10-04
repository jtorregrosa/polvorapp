using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ComparsaOrders.Contracts;
using static PolvorApp.Api.Tests.Infrastructure.OrderData;

namespace PolvorApp.Api.Tests.Orders;

/// <summary>The entries other modules read, e.g. for pickup proxies (add-distribution-planning, design D3): IEditionEntries.</summary>
public sealed class EditionEntriesTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private OrderTestHost _orders = null!;

    public async ValueTask InitializeAsync() => _orders = await OrderTestHost.StartAsync(postgres, mailpit);

    public async ValueTask DisposeAsync() => await _orders.DisposeAsync();

    [Fact]
    public async Task A_comparsas_entries_of_the_edition_are_listed()
    {
        var own = NewOrder(_orders.Current, _orders.Own.Id, OrderStatus.Submitted);
        var other = NewOrder(_orders.Current, _orders.Other.Id);
        var past = NewOrder(_orders.Previous, _orders.Own.Id, OrderStatus.Validated);
        var first = NewEntry(own, null);
        var second = NewEntry(own, null, ArquebusierStatus.Reserve);
        await _orders.Services.SaveOrdersAsync(own, other, past, first, second, NewEntry(other, null), NewEntry(past, null));

        var entries = await EntriesAsync(e => e.ListAsync(_orders.Current.Id, _orders.Own.Id, TestContext.Current.CancellationToken));

        Assert.Equal(new[] { first.Id, second.Id }.Order(), entries.Select(e => e.EntryId).Order());
        Assert.All(entries, e => Assert.Equal((own.Id, _orders.Current.Id, _orders.Own.Id, OrderStatus.Submitted), (e.OrderId, e.EditionId, e.ComparsaId, e.OrderStatus)));
    }

    [Fact]
    public async Task A_comparsa_without_an_order_has_no_entries() =>
        Assert.Empty(await EntriesAsync(e => e.ListAsync(_orders.Current.Id, _orders.Own.Id, TestContext.Current.CancellationToken)));

    [Fact]
    public async Task Entries_are_found_by_id_with_their_facts()
    {
        var (arquebusier, _) = await _orders.AddArquebusierAsync(_orders.Own.Id, "Titular Sintético");
        var order = NewOrder(_orders.Current, _orders.Own.Id, OrderStatus.Validated);
        var rented = NewEntry(order, arquebusier.Id);
        (rented.PowderKg, rented.WeaponSource, rented.RentalWeaponModelId, rented.Flask) = (2, WeaponSource.Rental, _orders.Offered.Id, FlaskOption.Rental1Kg);
        var reserve = NewEntry(order, null, ArquebusierStatus.Reserve);
        await _orders.Services.SaveOrdersAsync(order, rented, reserve);

        var entries = (await EntriesAsync(e => e.FindManyAsync([rented.Id, reserve.Id, Guid.CreateVersion7()], TestContext.Current.CancellationToken)))
            .ToDictionary(e => e.EntryId);

        Assert.Equal(2, entries.Count);
        var found = entries[rented.Id];
        Assert.Equal(
            (order.Id, _orders.Current.Id, _orders.Own.Id, OrderStatus.Validated, (Guid?)arquebusier.Id, true, 2, WeaponSource.Rental, (Guid?)_orders.Offered.Id, FlaskOption.Rental1Kg),
            (found.OrderId, found.EditionId, found.ComparsaId, found.OrderStatus, found.ArquebusierId, found.IsActive, found.PowderKg, found.WeaponSource, found.RentalWeaponModelId, found.Flask));
        Assert.Equal(new ExportedPerson(rented.FirstName, rented.LastName, rented.NationalId, rented.FederationId), found.Copy);
        Assert.False(entries[reserve.Id].IsActive);
        Assert.Null(entries[reserve.Id].ArquebusierId);
    }

    [Fact]
    public async Task The_order_status_of_each_prepared_comparsa_is_listed()
    {
        await _orders.Services.SaveOrdersAsync(
            NewOrder(_orders.Current, _orders.Own.Id, OrderStatus.Validated),
            NewOrder(_orders.Current, _orders.Other.Id, OrderStatus.Submitted),
            NewOrder(_orders.Previous, _orders.Inactive.Id, OrderStatus.Validated));

        var statuses = await EntriesAsync(e => e.ListOrderStatusesAsync(_orders.Current.Id, TestContext.Current.CancellationToken));

        Assert.Equal(new Dictionary<Guid, OrderStatus> { [_orders.Own.Id] = OrderStatus.Validated, [_orders.Other.Id] = OrderStatus.Submitted }, statuses);
    }

    [Fact]
    public async Task The_orders_of_an_edition_are_listed_with_their_comparsa_and_status()
    {
        var own = NewOrder(_orders.Current, _orders.Own.Id, OrderStatus.Submitted);
        await _orders.Services.SaveOrdersAsync(own, NewOrder(_orders.Previous, _orders.Inactive.Id, OrderStatus.Validated));

        var orders = await EntriesAsync(e => e.ListOrdersAsync(_orders.Current.Id, TestContext.Current.CancellationToken));

        Assert.Equal([new OrderSummary(own.Id, _orders.Own.Id, OrderStatus.Submitted)], orders);
    }

    [Fact]
    public async Task No_ids_find_nothing() =>
        Assert.Empty(await EntriesAsync(e => e.FindManyAsync([], TestContext.Current.CancellationToken)));

    [Fact]
    public async Task Too_many_ids_are_refused_without_reading()
    {
        Guid[] ids = [.. Enumerable.Range(0, IEditionEntries.MaxIds + 1).Select(_ => Guid.CreateVersion7())];

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => EntriesAsync(e => e.FindManyAsync(ids, TestContext.Current.CancellationToken)));
    }

    [Fact]
    public void Entry_facts_print_no_personal_data()
    {
        var facts = new EditionEntryFacts(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), OrderStatus.Draft, null, true, 1,
            WeaponSource.None, null, FlaskOption.None, new ExportedPerson("Nombre", "Apellido", "00000000T", 100001));

        Assert.DoesNotMatch("Nombre|Apellido|00000000T|100001", facts.ToString());
    }

    private async Task<T> EntriesAsync<T>(Func<IEditionEntries, Task<T>> read)
    {
        await using var scope = _orders.Services.CreateAsyncScope();
        return await read(scope.ServiceProvider.GetRequiredService<IEditionEntries>());
    }
}
