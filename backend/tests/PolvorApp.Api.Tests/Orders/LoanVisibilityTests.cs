using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.IdentityAccess.Contracts;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Orders;

/// <summary>Spec "Order visibility (BR-12)": the loan exceptions, for the lender's and the borrower's comparsas (design D9).</summary>
public sealed class LoanVisibilityTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private OrderTestHost _orders = null!;
    private HttpClient _otherChief = null!;

    public async ValueTask InitializeAsync()
    {
        _orders = await OrderTestHost.StartAsync(postgres, mailpit);
        var chief = await _orders.Host.CreateUserAsync("jefe.prestamista@example.test", UserRole.FiringChief);
        await _orders.Host.AssignAsync(_orders.Other.Id, chief.Id);
        _otherChief = await _orders.Host.SignInAsync(chief);
    }

    public async ValueTask DisposeAsync()
    {
        _otherChief.Dispose();
        await _orders.DisposeAsync();
    }

    [Fact]
    public async Task The_lenders_comparsa_sees_the_weapon_lent_and_nothing_else_of_the_borrowers_order()
    {
        var (lender, weapons) = await _orders.AddArquebusierAsync(_orders.Other.Id, "Dueña Sintética", weapons: 1);
        var (borrower, _) = await _orders.AddArquebusierAsync(_orders.Own.Id, "Prestataria Sintética");
        var borrowerOrder = await _orders.PrepareAsync(_orders.FiringChief, _orders.Own.Id);
        await LendAsync(borrowerOrder, borrower.Id, weapons[0].Id);
        var lenderOrder = await _orders.PrepareAsync(_otherChief, _orders.Other.Id);

        var lent = Assert.Single(lenderOrder.GetProperty("lentOut").EnumerateArray());
        using var foreign = await _otherChief.GetAsync($"/api/comparsa-orders/{Id(borrowerOrder)}", TestContext.Current.CancellationToken);

        Assert.Equal(("Dueña Sintética", "Prestataria Sintética", _orders.Own.Name, weapons[0].WeaponNumber, _orders.Offered.Label), (
            lent.GetProperty("lenderLastName").GetString(),
            lent.GetProperty("borrowerLastName").GetString(),
            lent.GetProperty("borrowerComparsaName").GetString(),
            lent.GetProperty("weaponNumber").GetString(),
            lent.GetProperty("weaponModel").GetProperty("label").GetString()));
        Assert.DoesNotContain("ownershipGuide", lent.GetRawText(), StringComparison.Ordinal);
        await AssertProblemAsync(foreign, HttpStatusCode.NotFound, "orders.notFound");
        Assert.DoesNotContain(lender.NationalId, lent.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_borrowers_comparsa_sees_the_lender_and_their_comparsa()
    {
        var (_, weapons) = await _orders.AddArquebusierAsync(_orders.Other.Id, "Dueño Sintético", weapons: 1);
        var (borrower, _) = await _orders.AddArquebusierAsync(_orders.Own.Id, "Prestatario Sintético");
        var order = await _orders.PrepareAsync(_orders.FiringChief, _orders.Own.Id);

        var saved = await LendAsync(order, borrower.Id, weapons[0].Id);

        var loan = Entry(saved, borrower.Id).GetProperty("loan");
        Assert.Equal(("Dueño Sintético", _orders.Other.Name), (loan.GetProperty("lenderLastName").GetString(), loan.GetProperty("lenderComparsaName").GetString()));
    }

    [Fact]
    public async Task Loans_of_other_editions_are_not_listed()
    {
        var (_, weapons) = await _orders.AddArquebusierAsync(_orders.Other.Id, "Antiguo Dueño Sintético", weapons: 1);
        var previousOrder = OrderData.NewOrder(_orders.Previous, _orders.Own.Id);
        var previousEntry = OrderData.NewEntry(previousOrder, null);
        previousEntry.WeaponSource = PolvorApp.ComparsaOrders.Entries.WeaponSource.Loan;
        var previousLoan = new PolvorApp.ComparsaOrders.Loans.WeaponLoan
        {
            Id = Guid.CreateVersion7(),
            EntryId = previousEntry.Id,
            LenderKind = PolvorApp.ComparsaOrders.Entries.LenderKind.Arquebusier,
            LenderOwnedWeaponId = weapons[0].Id,
            LenderComparsaId = _orders.Other.Id,
            CopiedAt = DateTimeOffset.UtcNow,
        };
        await _orders.Services.SaveOrdersAsync(previousOrder, previousEntry, previousLoan);

        var lenderOrder = await _orders.PrepareAsync(_otherChief, _orders.Other.Id);

        Assert.Empty(lenderOrder.GetProperty("lentOut").EnumerateArray());
    }

    private async Task<JsonElement> LendAsync(JsonElement order, Guid borrowerId, Guid weaponId)
    {
        var entry = Entry(order, borrowerId);
        using var response = await _orders.FiringChief.PutAsJsonAsync(
            $"/api/comparsa-orders/{Id(order)}/entries/{entry.GetProperty("id").GetString()}",
            new
            {
                version = entry.GetProperty("version").GetUInt32(),
                status = "ACTIVE",
                powderKg = 1,
                capsBoxes = 0,
                weaponSource = "LOAN",
                loan = new { ownedWeaponId = weaponId },
                flask = "NONE",
            },
            TestContext.Current.CancellationToken);
        return await ReadAsync<JsonElement>(response);
    }

    private static string Id(JsonElement order) => order.GetProperty("id").GetString()!;

    private static JsonElement Entry(JsonElement order, Guid arquebusierId) =>
        order.GetProperty("entries").EnumerateArray().Single(e => e.GetProperty("arquebusier").GetProperty("id").GetString() == arquebusierId.ToString());
}
