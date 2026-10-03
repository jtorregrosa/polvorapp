using System.Net;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.ComparsaOrders;
using PolvorApp.ComparsaOrders.Entries;
using PolvorApp.ComparsaOrders.Loans;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.FestivalEditions.Contracts;
using PolvorApp.IdentityAccess.Contracts;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;
using static PolvorApp.Api.Tests.Infrastructure.OrderData;
using static PolvorApp.Api.Tests.Infrastructure.RegistryData;

namespace PolvorApp.Api.Tests.Orders;

/// <summary>
/// Spec federation-catalog "Deleting comparsas and weapon models": orders are a reference that blocks
/// deletion, reported through the catalogue usage contract (add-comparsa-orders, design D5).
/// </summary>
public sealed class OrdersCatalogUsageTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private IdentityTestHost _host = null!;
    private HttpClient _admin = null!;

    public async ValueTask InitializeAsync()
    {
        _host = await IdentityTestHost.StartAsync(postgres, mailpit);
        _admin = await _host.SignInAsync(await _host.CreateUserAsync("admin.uso.pedidos@example.test", UserRole.Admin));
    }

    public async ValueTask DisposeAsync()
    {
        _admin.Dispose();
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task A_comparsa_with_an_order_cannot_be_deleted()
    {
        var comparsa = NewComparsa("Comparsa Sintética Con Pedido");
        await _host.Services.SaveCatalogAsync(comparsa);
        var edition = NewEdition(2031, EditionStatus.Closed);
        await _host.Services.SaveEditionsAsync(edition);
        await _host.Services.SaveOrdersAsync(NewOrder(edition, comparsa.Id));

        using var response = await _admin.DeleteAsync($"/api/comparsas/{comparsa.Id}", TestContext.Current.CancellationToken);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "comparsas.inUse");
        Assert.True(await WithUsageAsync(usage => usage.IsComparsaInUseAsync(comparsa.Id, TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task A_lender_comparsa_named_only_by_a_loan_is_in_use()
    {
        var (order, lenderComparsa) = await OrderAndOtherComparsaAsync();
        var entry = NewEntry(order, null);
        entry.WeaponSource = WeaponSource.Loan;
        var loan = new WeaponLoan
        {
            Id = Guid.CreateVersion7(),
            EntryId = entry.Id,
            LenderKind = LenderKind.Arquebusier,
            LenderComparsaId = lenderComparsa,
            CopiedAt = DateTimeOffset.UtcNow,
        };
        await _host.Services.SaveOrdersAsync(entry, loan);

        Assert.True(await WithUsageAsync(usage => usage.IsComparsaInUseAsync(lenderComparsa, TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task A_rented_model_cannot_be_deleted()
    {
        var model = await _admin.CreateRentableModelAsync("ARCABUZ ALQUILADO EN PEDIDO");
        var (order, _) = await OrderAndOtherComparsaAsync();
        var entry = NewEntry(order, null);
        (entry.WeaponSource, entry.RentalWeaponModelId) = (WeaponSource.Rental, model);
        await _host.Services.SaveOrdersAsync(entry);

        using var response = await _admin.DeleteAsync($"/api/weapon-models/{model}", TestContext.Current.CancellationToken);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "weaponModels.inUse");
        Assert.True(await WithUsageAsync(usage => usage.IsWeaponModelInUseAsync(model, TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task Models_in_owned_weapon_and_loan_copies_are_in_use()
    {
        var owned = await _admin.CreateRentableModelAsync("ARCABUZ PROPIO EN HISTORIAL");
        var lent = await _admin.CreateRentableModelAsync("ARCABUZ PRESTADO EN HISTORIAL", handedness: "LEFT");
        var (order, _) = await OrderAndOtherComparsaAsync();
        var ownedEntry = NewEntry(order, null);
        (ownedEntry.WeaponSource, ownedEntry.OwnedWeaponModelId) = (WeaponSource.Owned, owned);
        var borrower = NewEntry(order, null);
        borrower.WeaponSource = WeaponSource.Loan;
        var loan = new WeaponLoan
        {
            Id = Guid.CreateVersion7(),
            EntryId = borrower.Id,
            LenderKind = LenderKind.External,
            WeaponModelId = lent,
            CopiedAt = DateTimeOffset.UtcNow,
        };
        await _host.Services.SaveOrdersAsync(ownedEntry, borrower, loan);

        Assert.True(await WithUsageAsync(usage => usage.IsWeaponModelInUseAsync(owned, TestContext.Current.CancellationToken)));
        Assert.True(await WithUsageAsync(usage => usage.IsWeaponModelInUseAsync(lent, TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task Unused_comparsas_and_models_are_not_in_use()
    {
        Assert.False(await WithUsageAsync(usage => usage.IsComparsaInUseAsync(Guid.CreateVersion7(), TestContext.Current.CancellationToken)));
        Assert.False(await WithUsageAsync(usage => usage.IsWeaponModelInUseAsync(Guid.CreateVersion7(), TestContext.Current.CancellationToken)));
    }

    private async Task<(ComparsaOrders.Orders.ComparsaOrder Order, Guid OtherComparsa)> OrderAndOtherComparsaAsync()
    {
        var comparsa = NewComparsa("Comparsa Sintética Pedido Uso");
        var other = NewComparsa("Comparsa Sintética Prestamista Uso");
        await _host.Services.SaveCatalogAsync(comparsa, other);
        var edition = NewEdition(2031, EditionStatus.Closed);
        await _host.Services.SaveEditionsAsync(edition);
        var order = NewOrder(edition, comparsa.Id);
        await _host.Services.SaveOrdersAsync(order);
        return (order, other.Id);
    }

    private async Task<bool> WithUsageAsync(Func<OrdersCatalogUsage, Task<bool>> ask)
    {
        await using var scope = _host.Services.CreateAsyncScope();
        return await ask(scope.ServiceProvider.GetServices<ICatalogUsage>().OfType<OrdersCatalogUsage>().Single());
    }
}
