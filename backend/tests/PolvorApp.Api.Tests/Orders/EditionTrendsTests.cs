using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.FestivalEditions.Contracts;
using static PolvorApp.Api.Tests.Infrastructure.OrderData;

namespace PolvorApp.Api.Tests.Orders;

/// <summary>
/// Spec "Edition trends (UC-07)" (add-statistics-trends, design D1): one row per started edition, the
/// 10 most recent, counted from the entries of every prepared order in the comparsas asked for.
/// The host has the 2030 edition closed and 2031 in progress; all data is synthetic.
/// </summary>
public sealed class EditionTrendsTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private OrderTestHost _orders = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _orders = await OrderTestHost.StartAsync(postgres, mailpit);

    public async ValueTask DisposeAsync() => await _orders.DisposeAsync();

    [Fact]
    public async Task Draft_editions_are_left_out_and_the_edition_in_progress_is_provisional()
    {
        var draft = NewEdition(2032, EditionStatus.Draft);
        await _orders.Services.SaveEditionsAsync(draft);

        var rows = await TrendsAsync(null);

        Assert.Equal([(2030, false), (2031, true)], rows.Select(r => (r.Year, r.Provisional)));
    }

    [Fact]
    public async Task Only_the_ten_most_recent_started_editions_are_returned_sorted_by_year()
    {
        await _orders.Services.SaveEditionsAsync([.. Enumerable.Range(2018, 12).Select(year => (object)NewEdition(year, EditionStatus.Closed))]);

        var rows = await TrendsAsync(null);

        Assert.Equal(Enumerable.Range(2022, 10), rows.Select(r => r.Year));
        Assert.All(rows.Where(r => r.Year < 2030), r => Assert.Equal((0, 0), (r.Active, r.Reserve)));
    }

    [Fact]
    public async Task Each_row_counts_statuses_powder_caps_weapon_sources_rentals_and_flasks()
    {
        var order = NewOrder(_orders.Current, _orders.Own.Id, OrderStatus.Submitted);
        var owned = NewEntry(order, await PersonAsync());
        (owned.PowderKg, owned.CapsBoxes, owned.CapsType, owned.WeaponSource, owned.Flask) = (2, 3, CapsType.Normal, WeaponSource.Owned, FlaskOption.Owned);
        var rented = NewEntry(order, await PersonAsync());
        (rented.PowderKg, rented.WeaponSource, rented.RentalWeaponModelId, rented.Flask) = (1, WeaponSource.Rental, _orders.Offered.Id, FlaskOption.Rental2Kg);
        var lent = NewEntry(order, await PersonAsync());
        (lent.PowderKg, lent.CapsBoxes, lent.CapsType, lent.WeaponSource, lent.Flask) = (2, 1, CapsType.Small, WeaponSource.Loan, FlaskOption.Rental1Kg);
        var carrier = NewEntry(order, await PersonAsync());
        carrier.PowderKg = 1;
        await _orders.Services.SaveOrdersAsync(order, owned, rented, lent, carrier, NewEntry(order, await PersonAsync(), ArquebusierStatus.Reserve));

        var row = (await TrendsAsync(null)).Single(r => r.Year == 2031);

        Assert.Equal((4, 1, 6, 4), (row.Active, row.Reserve, row.PowderKg, row.CapsBoxes));
        Assert.Equal((1, 1, 1, 1), (row.Owned, row.Rental, row.Loan, row.NoWeapon));
        Assert.Equal(new Dictionary<Guid, int> { [_orders.Offered.Id] = 1 }, row.RentalsByModel);
        Assert.Equal(2, row.FlaskRentals);
    }

    [Fact]
    public async Task First_year_counts_active_entries_with_no_earlier_active_entry_and_is_unknown_for_the_first_edition_with_orders()
    {
        var veteran = await PersonAsync();
        var newcomer = await PersonAsync();
        var wasReserve = await PersonAsync();
        var previous = NewOrder(_orders.Previous, _orders.Other.Id, OrderStatus.Validated);
        var current = NewOrder(_orders.Current, _orders.Own.Id);
        await _orders.Services.SaveOrdersAsync(
            previous,
            NewEntry(previous, veteran),
            NewEntry(previous, wasReserve, ArquebusierStatus.Reserve),
            current,
            NewEntry(current, veteran),
            NewEntry(current, newcomer),
            NewEntry(current, wasReserve),
            NewEntry(current, null));

        var rows = await TrendsAsync(null);

        Assert.Null(rows.Single(r => r.Year == 2030).FirstYear);
        // The veteran moved comparsa but was active before; a deleted arquebusier cannot be judged.
        Assert.Equal(2, rows.Single(r => r.Year == 2031).FirstYear);
    }

    [Fact]
    public async Task Within_one_comparsa_an_arquebusier_active_earlier_in_another_is_not_in_their_first_year()
    {
        var veteran = await PersonAsync();
        var previous = NewOrder(_orders.Previous, _orders.Own.Id, OrderStatus.Validated);
        var current = NewOrder(_orders.Current, _orders.Other.Id);
        await _orders.Services.SaveOrdersAsync(previous, NewEntry(previous, veteran), current, NewEntry(current, veteran), NewEntry(current, await PersonAsync()));

        var row = (await TrendsAsync([_orders.Other.Id])).Single(r => r.Year == 2031);

        Assert.Equal((2, 1), (row.Active, row.FirstYear));
    }

    [Fact]
    public async Task The_oldest_edition_of_the_window_knows_its_first_year_when_older_editions_have_orders()
    {
        var older = Enumerable.Range(2019, 11).Select(year => NewEdition(year, EditionStatus.Closed)).ToList();
        await _orders.Services.SaveEditionsAsync([.. older.Cast<object>()]);
        var veteran = await PersonAsync();
        var first = NewOrder(older[0], _orders.Own.Id, OrderStatus.Validated);
        var windowStart = NewOrder(older[3], _orders.Own.Id, OrderStatus.Validated);
        await _orders.Services.SaveOrdersAsync(first, NewEntry(first, veteran), windowStart, NewEntry(windowStart, veteran), NewEntry(windowStart, await PersonAsync()));

        var rows = await TrendsAsync(null);

        // 2019–2021 fall out of the 10 most recent (2022–2031), yet their orders make 2022 known.
        Assert.Equal(2022, rows[0].Year);
        Assert.Equal(1, rows[0].FirstYear);
    }

    [Fact]
    public async Task Active_entries_are_counted_per_comparsa_with_their_arquebusiers_for_the_gender_count()
    {
        var own = NewOrder(_orders.Current, _orders.Own.Id);
        var other = NewOrder(_orders.Current, _orders.Other.Id);
        var known = await PersonAsync();
        await _orders.Services.SaveOrdersAsync(
            own,
            NewEntry(own, known),
            NewEntry(own, null),
            NewEntry(own, await PersonAsync(), ArquebusierStatus.Reserve),
            other,
            NewEntry(other, await PersonAsync()));

        var row = (await TrendsAsync(null)).Single(r => r.Year == 2031);

        Assert.Equal(new Dictionary<Guid, int> { [_orders.Own.Id] = 2, [_orders.Other.Id] = 1 }, row.ActiveByComparsa);
        Assert.Equal(3, row.Active);
        Assert.Equal(2, row.ActiveArquebusierIds.Count);
        Assert.Contains(known, row.ActiveArquebusierIds);
    }

    [Fact]
    public async Task Only_the_comparsas_asked_for_are_counted()
    {
        var own = NewOrder(_orders.Current, _orders.Own.Id);
        var other = NewOrder(_orders.Current, _orders.Other.Id);
        await _orders.Services.SaveOrdersAsync(own, NewEntry(own, await PersonAsync()), other, NewEntry(other, await PersonAsync()), NewEntry(other, await PersonAsync()));

        var row = (await TrendsAsync([_orders.Own.Id])).Single(r => r.Year == 2031);

        Assert.Equal(1, row.Active);
        Assert.Equal(new Dictionary<Guid, int> { [_orders.Own.Id] = 1 }, row.ActiveByComparsa);
    }

    [Fact]
    public async Task An_empty_scope_counts_nothing_but_still_lists_the_editions()
    {
        var own = NewOrder(_orders.Current, _orders.Own.Id);
        await _orders.Services.SaveOrdersAsync(own, NewEntry(own, await PersonAsync()));

        var rows = await TrendsAsync([]);

        Assert.Equal([2030, 2031], rows.Select(r => r.Year));
        Assert.All(rows, r => Assert.Equal(0, r.Active));
    }

    /// <summary>A synthetic arquebusier in the registry (entries reference the registry).</summary>
    private async Task<Guid> PersonAsync() => (await _orders.AddArquebusierAsync(_orders.Own.Id, "Tendencia Sintética")).Arquebusier.Id;

    private async Task<IReadOnlyList<EditionTrendRow>> TrendsAsync(IReadOnlyCollection<Guid>? comparsaIds)
    {
        await using var scope = _orders.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IEditionTrends>().ListAsync(comparsaIds, Token);
    }
}
