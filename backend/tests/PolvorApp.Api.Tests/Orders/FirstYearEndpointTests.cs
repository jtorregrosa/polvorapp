using System.Net;
using System.Text.Json;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.ComparsaOrders.Entries;
using PolvorApp.FestivalEditions.Contracts;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;
using static PolvorApp.Api.Tests.Infrastructure.OrderData;

namespace PolvorApp.Api.Tests.Orders;

/// <summary>
/// Spec "First year (UC-07)" on the arquebusier detail and the order entries, and the delete
/// confirmation's data of "Deleting an arquebusier (UC-05, BR-14)" (design D5).
/// </summary>
public sealed class FirstYearEndpointTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private OrderTestHost _orders = null!;

    public async ValueTask InitializeAsync() => _orders = await OrderTestHost.StartAsync(postgres, mailpit);

    public async ValueTask DisposeAsync() => await _orders.DisposeAsync();

    [Fact]
    public async Task The_detail_shows_the_first_year_once_an_earlier_edition_has_orders()
    {
        var (newcomer, _) = await _orders.AddArquebusierAsync(_orders.Own.Id, "Novato Sintético");
        var (veteran, _) = await _orders.AddArquebusierAsync(_orders.Own.Id, "Veterano Sintético");
        Assert.Equal(JsonValueKind.Null, (await DetailAsync(newcomer.Id)).GetProperty("firstYear").ValueKind);

        var previous = NewOrder(_orders.Previous, _orders.Own.Id, OrderStatus.Validated);
        await _orders.Services.SaveOrdersAsync(previous, NewEntry(previous, veteran.Id));

        Assert.True((await DetailAsync(newcomer.Id)).GetProperty("firstYear").GetBoolean());
        Assert.False((await DetailAsync(veteran.Id)).GetProperty("firstYear").GetBoolean());
    }

    [Fact]
    public async Task Without_an_edition_in_progress_the_detail_has_no_first_year()
    {
        var (arquebusier, _) = await _orders.AddArquebusierAsync(_orders.Own.Id, "Sin Edición Sintético");
        var previous = NewOrder(_orders.Previous, _orders.Own.Id, OrderStatus.Validated);
        await _orders.Services.SaveOrdersAsync(previous);
        await _orders.SetCurrentStatusAsync(EditionStatus.Closed);

        Assert.Equal(JsonValueKind.Null, (await DetailAsync(arquebusier.Id)).GetProperty("firstYear").ValueKind);
    }

    [Fact]
    public async Task The_detail_tells_what_a_deletion_does_to_the_orders()
    {
        var (arquebusier, weapons) = await _orders.AddArquebusierAsync(_orders.Own.Id, "Borrable Sintético", weapons: 1);
        var (borrower, _) = await _orders.AddArquebusierAsync(_orders.Other.Id, "Prestatario Sintético");
        var previous = NewOrder(_orders.Previous, _orders.Own.Id, OrderStatus.Validated);
        var current = NewOrder(_orders.Current, _orders.Own.Id, OrderStatus.Submitted);
        var other = NewOrder(_orders.Current, _orders.Other.Id);
        var borrowed = NewEntry(other, borrower.Id);
        borrowed.WeaponSource = WeaponSource.Loan;
        await _orders.Services.SaveOrdersAsync(
            previous, current, other, NewEntry(previous, arquebusier.Id), NewEntry(current, arquebusier.Id), borrowed,
            new PolvorApp.ComparsaOrders.Loans.WeaponLoan
            {
                Id = Guid.CreateVersion7(),
                EntryId = borrowed.Id,
                LenderKind = LenderKind.Arquebusier,
                LenderOwnedWeaponId = weapons[0].Id,
                LenderFirstName = arquebusier.FirstName,
                LenderLastName = arquebusier.LastName,
                LenderNationalId = arquebusier.NationalId,
                LenderComparsaId = _orders.Own.Id,
                WeaponNumber = weapons[0].WeaponNumber,
                CopiedAt = DateTimeOffset.UtcNow,
            });

        var impact = (await DetailAsync(arquebusier.Id, _orders.FiringChief)).GetProperty("deletionImpact");

        var entry = impact.GetProperty("currentEntry");
        Assert.Equal(
            (2031, _orders.Own.Name, "SUBMITTED", true),
            (entry.GetProperty("editionYear").GetInt32(), entry.GetProperty("comparsaName").GetString(),
             entry.GetProperty("orderStatus").GetString(), entry.GetProperty("willBeRemoved").GetBoolean()));
        Assert.Equal((1, true), (impact.GetProperty("lentWeapons").GetInt32(), impact.GetProperty("hasPastEntries").GetBoolean()));
    }

    [Fact]
    public async Task An_arquebusier_without_orders_has_an_empty_deletion_impact()
    {
        var (arquebusier, _) = await _orders.AddArquebusierAsync(_orders.Own.Id, "Sin Pedido Sintético");

        var impact = (await DetailAsync(arquebusier.Id)).GetProperty("deletionImpact");

        Assert.Equal(
            (JsonValueKind.Null, 0, false),
            (impact.GetProperty("currentEntry").ValueKind, impact.GetProperty("lentWeapons").GetInt32(), impact.GetProperty("hasPastEntries").GetBoolean()));
    }

    [Fact]
    public async Task Order_entries_carry_the_first_year_of_their_edition()
    {
        var (newcomer, _) = await _orders.AddArquebusierAsync(_orders.Own.Id, "Novata Sintética");
        var (veteran, _) = await _orders.AddArquebusierAsync(_orders.Own.Id, "Veterana Sintética");
        var previous = NewOrder(_orders.Previous, _orders.Own.Id, OrderStatus.Validated);
        var current = NewOrder(_orders.Current, _orders.Own.Id);
        var gone = NewEntry(current, null);
        gone.LastName = "Ausente Sintético";
        await _orders.Services.SaveOrdersAsync(
            previous, current, NewEntry(previous, veteran.Id), NewEntry(current, newcomer.Id), NewEntry(current, veteran.Id), gone);

        var order = await OrderTestHost.GetOrderAsync(_orders.FiringChief, current.Id);
        var pastOrder = await OrderTestHost.GetOrderAsync(_orders.Admin, previous.Id);

        var flags = order.GetProperty("entries").EnumerateArray().ToDictionary(
            e => e.GetProperty("arquebusier").GetProperty("lastName").GetString()!,
            e => e.GetProperty("firstYear"));
        Assert.True(flags["Novata Sintética"].GetBoolean());
        Assert.False(flags["Veterana Sintética"].GetBoolean());
        Assert.Equal(JsonValueKind.Null, flags["Ausente Sintético"].ValueKind);
        Assert.Equal(JsonValueKind.Null, Assert.Single(pastOrder.GetProperty("entries").EnumerateArray()).GetProperty("firstYear").ValueKind);
    }

    private async Task<JsonElement> DetailAsync(Guid arquebusierId, HttpClient? client = null)
    {
        using var response = await (client ?? _orders.Admin).GetAsync($"/api/arquebusiers/{arquebusierId}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadAsync<JsonElement>(response);
    }
}
