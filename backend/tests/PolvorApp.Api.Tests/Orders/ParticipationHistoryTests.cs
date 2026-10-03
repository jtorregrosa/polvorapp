using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.ComparsaOrders.Entries;
using PolvorApp.ComparsaOrders.Loans;
using static PolvorApp.Api.Tests.Infrastructure.OrderData;

namespace PolvorApp.Api.Tests.Orders;

/// <summary>
/// Spec "First year (UC-07)" and the delete confirmation of "Deleting an arquebusier (UC-05, BR-14)":
/// <see cref="IParticipationHistory"/> (design D5).
/// </summary>
public sealed class ParticipationHistoryTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private OrderTestHost _orders = null!;

    public async ValueTask InitializeAsync() => _orders = await OrderTestHost.StartAsync(postgres, mailpit);

    public async ValueTask DisposeAsync() => await _orders.DisposeAsync();

    [Fact]
    public async Task The_first_year_is_unknown_without_an_earlier_order()
    {
        var (arquebusier, _) = await _orders.AddArquebusierAsync(_orders.Own.Id, "Nuevo Sintético");
        var current = NewOrder(_orders.Current, _orders.Own.Id);
        await _orders.Services.SaveOrdersAsync(current, NewEntry(current, arquebusier.Id));

        var result = await FirstYearAsync(2031, arquebusier.Id);

        Assert.False(result.Known);
        Assert.Null(result.Of(arquebusier.Id));
    }

    [Fact]
    public async Task Only_an_earlier_active_entry_ends_the_first_year()
    {
        var (veteran, _) = await _orders.AddArquebusierAsync(_orders.Own.Id, "Veterano Sintético");
        var (reserve, _) = await _orders.AddArquebusierAsync(_orders.Own.Id, "Reserva Sintética");
        var (newcomer, _) = await _orders.AddArquebusierAsync(_orders.Own.Id, "Novato Sintético");
        var previous = NewOrder(_orders.Previous, _orders.Other.Id, OrderStatus.Validated);
        await _orders.Services.SaveOrdersAsync(
            previous,
            NewEntry(previous, veteran.Id),
            NewEntry(previous, reserve.Id, ArquebusierStatus.Reserve));

        var result = await FirstYearAsync(2031, veteran.Id, reserve.Id, newcomer.Id);

        Assert.True(result.Known);
        Assert.Equal(new HashSet<Guid> { reserve.Id, newcomer.Id }, result.FirstYearIds);
    }

    [Fact]
    public async Task Entries_of_the_same_or_a_later_edition_do_not_count()
    {
        var (arquebusier, _) = await _orders.AddArquebusierAsync(_orders.Own.Id, "Actual Sintético");
        var previous = NewOrder(_orders.Previous, _orders.Other.Id, OrderStatus.Validated);
        var current = NewOrder(_orders.Current, _orders.Own.Id);
        await _orders.Services.SaveOrdersAsync(previous, current, NewEntry(current, arquebusier.Id));

        Assert.True((await FirstYearAsync(2031, arquebusier.Id)).Of(arquebusier.Id));
        Assert.False((await FirstYearAsync(2030, arquebusier.Id)).Known);
    }

    [Fact]
    public async Task An_entry_no_longer_linked_to_the_registry_is_ignored()
    {
        var (arquebusier, _) = await _orders.AddArquebusierAsync(_orders.Own.Id, "Desvinculado Sintético");
        var previous = NewOrder(_orders.Previous, _orders.Own.Id, OrderStatus.Validated);
        var orphan = NewEntry(previous, null);
        (orphan.FirstName, orphan.LastName) = (arquebusier.FirstName, arquebusier.LastName);
        await _orders.Services.SaveOrdersAsync(previous, orphan);

        Assert.True((await FirstYearAsync(2031, arquebusier.Id)).Of(arquebusier.Id));
    }

    [Fact]
    public async Task The_deletion_impact_names_the_current_entry_and_whether_it_will_be_removed()
    {
        var (arquebusier, weapons) = await _orders.AddArquebusierAsync(_orders.Own.Id, "Borrable Sintético", weapons: 2);
        var (borrower, _) = await _orders.AddArquebusierAsync(_orders.Other.Id, "Prestatario Sintético");
        var previous = NewOrder(_orders.Previous, _orders.Own.Id, OrderStatus.Validated);
        var current = NewOrder(_orders.Current, _orders.Own.Id, OrderStatus.Submitted);
        var otherOrder = NewOrder(_orders.Current, _orders.Other.Id);
        var borrowed = NewEntry(otherOrder, borrower.Id);
        borrowed.WeaponSource = WeaponSource.Loan;
        await _orders.Services.SaveOrdersAsync(
            previous,
            current,
            otherOrder,
            NewEntry(previous, arquebusier.Id),
            NewEntry(current, arquebusier.Id),
            borrowed,
            Loan(borrowed, weapons[0].Id));

        var open = await DeletionImpactAsync(arquebusier.Id, weapons.Select(w => w.Id));
        await _orders.SetOrdersOpenAsync(false);
        var closed = await DeletionImpactAsync(arquebusier.Id, weapons.Select(w => w.Id));

        Assert.Equal(new DeletionImpact(new CurrentEntryImpact(2031, _orders.Own.Id, OrderStatus.Submitted, WillBeRemoved: true), 1, HasPastEntries: true), open);
        Assert.False(closed.CurrentEntry!.WillBeRemoved);
    }

    [Fact]
    public async Task An_arquebusier_without_entries_or_loans_has_no_deletion_impact()
    {
        var (arquebusier, weapons) = await _orders.AddArquebusierAsync(_orders.Own.Id, "Sin Pedidos Sintético", weapons: 1);

        Assert.Equal(DeletionImpact.None, await DeletionImpactAsync(arquebusier.Id, weapons.Select(w => w.Id)));
    }

    [Fact]
    public async Task Without_an_edition_in_progress_every_entry_is_past()
    {
        var (arquebusier, weapons) = await _orders.AddArquebusierAsync(_orders.Own.Id, "Histórico Sintético", weapons: 1);
        var current = NewOrder(_orders.Current, _orders.Own.Id);
        await _orders.Services.SaveOrdersAsync(current, NewEntry(current, arquebusier.Id));
        await _orders.SetCurrentStatusAsync(PolvorApp.FestivalEditions.Contracts.EditionStatus.Closed);

        Assert.Equal(new DeletionImpact(null, 0, HasPastEntries: true), await DeletionImpactAsync(arquebusier.Id, weapons.Select(w => w.Id)));
    }

    private WeaponLoan Loan(EditionEntry entry, Guid ownedWeaponId) => new()
    {
        Id = Guid.CreateVersion7(),
        EntryId = entry.Id,
        LenderKind = LenderKind.Arquebusier,
        LenderOwnedWeaponId = ownedWeaponId,
        LenderFirstName = "Prestamista",
        LenderLastName = "Sintético",
        LenderNationalId = RegistryData.NextIdentity().NationalId,
        LenderComparsaId = _orders.Own.Id,
        WeaponNumber = "PRESTADA-1",
        CopiedAt = DateTimeOffset.UtcNow,
    };

    private async Task<FirstYearResult> FirstYearAsync(int editionYear, params Guid[] arquebusierIds)
    {
        await using var scope = _orders.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IParticipationHistory>()
            .FirstYearAsync(editionYear, arquebusierIds, TestContext.Current.CancellationToken);
    }

    private async Task<DeletionImpact> DeletionImpactAsync(Guid arquebusierId, IEnumerable<Guid> ownedWeaponIds)
    {
        await using var scope = _orders.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IParticipationHistory>()
            .GetDeletionImpactAsync(arquebusierId, [.. ownedWeaponIds], TestContext.Current.CancellationToken);
    }
}
