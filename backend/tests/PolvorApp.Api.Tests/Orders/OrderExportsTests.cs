using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.ComparsaOrders.Loans;
using static PolvorApp.Api.Tests.Infrastructure.OrderData;

namespace PolvorApp.Api.Tests.Orders;

/// <summary>What the exports read from the orders (change add-exports, design D2): IOrderExports.</summary>
public sealed class OrderExportsTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private OrderTestHost _orders = null!;

    public async ValueTask InitializeAsync() => _orders = await OrderTestHost.StartAsync(postgres, mailpit);

    public async ValueTask DisposeAsync() => await _orders.DisposeAsync();

    [Fact]
    public async Task Only_the_validated_orders_of_the_edition_are_listed()
    {
        var validated = NewOrder(_orders.Current, _orders.Own.Id, OrderStatus.Validated);
        var submitted = NewOrder(_orders.Current, _orders.Other.Id, OrderStatus.Submitted);
        var past = NewOrder(_orders.Previous, _orders.Own.Id, OrderStatus.Validated);
        await _orders.Services.SaveOrdersAsync(validated, submitted, past, NewEntry(validated, null), NewEntry(submitted, null), NewEntry(past, null));

        var orders = await ExportsAsync(e => e.ListValidatedAsync(_orders.Current.Id, TestContext.Current.CancellationToken));

        var order = Assert.Single(orders);
        Assert.Equal((validated.Id, _orders.Own.Id, OrderStatus.Validated), (order.OrderId, order.ComparsaId, order.Status));
        Assert.Single(order.Entries);
    }

    [Theory]
    [InlineData(OrderStatus.Draft)]
    [InlineData(OrderStatus.Submitted)]
    [InlineData(OrderStatus.Returned)]
    [InlineData(OrderStatus.Validated)]
    public async Task A_comparsas_order_is_found_in_any_status(OrderStatus status)
    {
        var order = NewOrder(_orders.Current, _orders.Own.Id, status);
        if (status == OrderStatus.Returned)
        {
            order.ReturnReason = "Motivo sintético";
        }

        await _orders.Services.SaveOrdersAsync(order, NewEntry(order, null));

        var found = await ExportsAsync(e => e.FindAsync(_orders.Current.Id, _orders.Own.Id, TestContext.Current.CancellationToken));

        Assert.Equal((order.Id, status, 1), (found!.OrderId, found.Status, found.Entries.Count));
    }

    [Fact]
    public async Task An_edition_without_validated_orders_lists_none()
    {
        await _orders.Services.SaveOrdersAsync(NewOrder(_orders.Current, _orders.Own.Id, OrderStatus.Submitted));

        Assert.Empty(await ExportsAsync(e => e.ListValidatedAsync(_orders.Current.Id, TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task Another_editions_order_of_the_comparsa_is_not_found()
    {
        await _orders.Services.SaveOrdersAsync(NewOrder(_orders.Previous, _orders.Own.Id, OrderStatus.Validated));

        Assert.Null(await ExportsAsync(e => e.FindAsync(_orders.Current.Id, _orders.Own.Id, TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task A_registered_lender_carries_their_comparsa()
    {
        var (lender, weapons) = await _orders.AddArquebusierAsync(_orders.Other.Id, "Prestamista Registrado", weapons: 1);
        var order = NewOrder(_orders.Current, _orders.Own.Id, OrderStatus.Validated);
        var borrowed = NewEntry(order, null);
        borrowed.WeaponSource = WeaponSource.Loan;
        var loan = new WeaponLoan
        {
            Id = Guid.CreateVersion7(),
            EntryId = borrowed.Id,
            LenderKind = LenderKind.Arquebusier,
            LenderOwnedWeaponId = weapons[0].Id,
            LenderComparsaId = _orders.Other.Id,
            LenderFirstName = lender.FirstName,
            LenderLastName = lender.LastName,
            LenderNationalId = lender.NationalId,
            WeaponModelId = _orders.Offered.Id,
            WeaponNumber = "5-29",
            OwnershipGuideNumber = "GUIA-EXP-REG",
            CopiedAt = DateTimeOffset.UtcNow,
        };
        await _orders.Services.SaveOrdersAsync(order, borrowed, loan);

        var lent = (await ExportsAsync(e => e.FindAsync(_orders.Current.Id, _orders.Own.Id, TestContext.Current.CancellationToken)))!
            .Entries.Single().Loan!;

        Assert.Equal((LenderKind.Arquebusier, (Guid?)_orders.Other.Id, lender.NationalId, (int?)null),
            (lent.LenderKind, lent.LenderComparsaId, lent.Lender.NationalId, lent.Lender.FederationId));
    }

    [Fact]
    public async Task A_comparsa_without_an_order_has_none()
    {
        var found = await ExportsAsync(e => e.FindAsync(_orders.Current.Id, _orders.Own.Id, TestContext.Current.CancellationToken));

        Assert.Null(found);
    }

    [Fact]
    public async Task Entries_carry_their_values_links_copies_and_loans()
    {
        var (owner, weapons) = await _orders.AddArquebusierAsync(_orders.Own.Id, "Dueño Exportación", weapons: 1);
        var order = NewOrder(_orders.Current, _orders.Own.Id, OrderStatus.Validated);
        var owned = NewEntry(order, owner.Id);
        (owned.PowderKg, owned.CapsBoxes, owned.CapsType, owned.WeaponSource, owned.OwnedWeaponId, owned.Flask) =
            (2, 3, CapsType.Small, WeaponSource.Owned, weapons[0].Id, FlaskOption.Rental2Kg);
        (owned.FirstName, owned.LastName, owned.NationalId, owned.FederationId) = (owner.FirstName, owner.LastName, owner.NationalId, owner.FederationId);
        (owned.OwnedWeaponModelId, owned.OwnedWeaponNumber, owned.OwnedWeaponGuideNumber) = (_orders.Offered.Id, "77-31", "GUIA-EXP-1");
        var rented = NewEntry(order, null);
        (rented.WeaponSource, rented.RentalWeaponModelId) = (WeaponSource.Rental, _orders.Offered.Id);
        var borrowed = NewEntry(order, null);
        borrowed.WeaponSource = WeaponSource.Loan;
        var loan = new WeaponLoan
        {
            Id = Guid.CreateVersion7(),
            EntryId = borrowed.Id,
            LenderKind = LenderKind.External,
            LenderFirstName = "Prestamista",
            LenderLastName = "Externo Sintético",
            LenderNationalId = RegistryData.NextIdentity().NationalId,
            WeaponModelId = _orders.Offered.Id,
            WeaponNumber = "12-30",
            OwnershipGuideNumber = "GUIA-EXP-EXT",
            CopiedAt = DateTimeOffset.UtcNow,
        };
        var reserve = NewEntry(order, null, ArquebusierStatus.Reserve);
        await _orders.Services.SaveOrdersAsync(order, owned, rented, borrowed, loan, reserve);

        var entries = (await ExportsAsync(e => e.FindAsync(_orders.Current.Id, _orders.Own.Id, TestContext.Current.CancellationToken)))!
            .Entries.ToDictionary(e => e.EntryId);

        var first = entries[owned.Id];
        Assert.Equal((owner.Id, true, 2, 3, CapsType.Small, WeaponSource.Owned, weapons[0].Id, FlaskOption.Rental2Kg),
            (first.ArquebusierId, first.IsActive, first.PowderKg, first.CapsBoxes, first.CapsType, first.WeaponSource, first.OwnedWeaponId, first.Flask));
        Assert.Equal(new ExportedPerson(owner.FirstName, owner.LastName, owner.NationalId, owner.FederationId), first.Person);
        Assert.Equal(new ExportedWeapon(_orders.Offered.Id, "77-31", "GUIA-EXP-1"), first.OwnedWeapon);
        Assert.Null(first.Loan);
        Assert.Equal((_orders.Offered.Id, (ExportedWeapon?)null), (entries[rented.Id].RentalWeaponModelId, entries[rented.Id].OwnedWeapon));
        var lent = entries[borrowed.Id].Loan!;
        Assert.Equal((LenderKind.External, "Externo Sintético", (Guid?)null), (lent.LenderKind, lent.Lender.LastName, lent.LenderComparsaId));
        Assert.Equal(new ExportedWeapon(_orders.Offered.Id, "12-30", "GUIA-EXP-EXT"), lent.Weapon);
        Assert.False(entries[reserve.Id].IsActive);
    }

    [Fact]
    public async Task An_entry_of_a_deleted_arquebusier_keeps_its_copy()
    {
        var order = NewOrder(_orders.Current, _orders.Own.Id, OrderStatus.Validated);
        var history = NewEntry(order, arquebusierId: null);
        await _orders.Services.SaveOrdersAsync(order, history);

        var entry = Assert.Single((await ExportsAsync(e => e.ListValidatedAsync(_orders.Current.Id, TestContext.Current.CancellationToken)))[0].Entries);

        Assert.Null(entry.ArquebusierId);
        Assert.Equal(("Arcabucero", "Sintético Copia", history.NationalId), (entry.Person.FirstName, entry.Person.LastName, entry.Person.NationalId));
    }

    [Fact]
    public void Records_print_no_personal_data()
    {
        var person = new ExportedPerson("Nombre", "Apellido", "00000000T", 100001);
        var weapon = new ExportedWeapon(Guid.NewGuid(), "1-23", "GUIA-SECRETA");
        var entry = new ExportedEntry(Guid.NewGuid(), Guid.NewGuid(), true, 1, 0, null, WeaponSource.Owned, null, null, FlaskOption.None, person, weapon,
            new ExportedLoan(LenderKind.External, person, null, weapon));

        string[] printed = [person.ToString(), weapon.ToString(), entry.ToString(), entry.Loan!.ToString(),
            new ExportedOrder(Guid.NewGuid(), Guid.NewGuid(), OrderStatus.Draft, [entry]).ToString()];

        Assert.All(printed, text => Assert.DoesNotMatch("Nombre|Apellido|00000000T|GUIA-SECRETA|1-23", text));
    }

    private async Task<T> ExportsAsync<T>(Func<IOrderExports, Task<T>> read)
    {
        await using var scope = _orders.Services.CreateAsyncScope();
        return await read(scope.ServiceProvider.GetRequiredService<IOrderExports>());
    }
}
