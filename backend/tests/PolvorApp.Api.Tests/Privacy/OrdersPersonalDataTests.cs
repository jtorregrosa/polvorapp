using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.ArquebusierRegistry.Persistence;
using PolvorApp.AuditPrivacy.Contracts;
using PolvorApp.AuditPrivacy.Privacy;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.ComparsaOrders.Entries;
using PolvorApp.ComparsaOrders.Loans;
using PolvorApp.ComparsaOrders.Persistence;
using PolvorApp.FederationCatalog.WeaponModels;
using PolvorApp.FestivalEditions.Contracts;
using PolvorApp.SharedKernel.Auditing;
using static PolvorApp.Api.Tests.Infrastructure.OrderData;

namespace PolvorApp.Api.Tests.Privacy;

/// <summary>Spec audit-privacy "Erasing a person's data", comparsa-orders "Erased entries", design D7: the orders' part.</summary>
public sealed class OrdersPersonalDataTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private readonly WeaponModel _model = RegistryData.NewWeaponModel("ARCABUZ SINTÉTICO HISTÓRICO");
    private RegistryTestHost _registry = null!;

    public async ValueTask InitializeAsync()
    {
        _registry = await RegistryTestHost.StartAsync(postgres, mailpit);
        await _registry.Services.SaveCatalogAsync(_model);
    }

    public async ValueTask DisposeAsync() => await _registry.DisposeAsync();

    [Fact]
    public async Task A_former_arquebusier_is_found_by_the_copy_and_anonymised_keeping_the_figures()
    {
        var order = await OrderAsync(2029, EditionStatus.Closed, OrderStatus.Validated);
        var entry = NewEntry(order, null);
        entry.PowderKg = 2;
        await _registry.Services.SaveOrdersAsync(entry);
        var person = new PersonalDataSubject.Person(entry.NationalId!);

        var summary = await RunAsync(r => r.DescribeAsync(person, TestContext.Current.CancellationToken));
        var parts = await RunAsync(r => r.ExportAsync(person, TestContext.Current.CancellationToken));
        var result = await RunAsync(r => r.EraseAsync(person, Audit, TestContext.Current.CancellationToken));

        var described = Assert.Single(summary.Entries);
        Assert.Equal((2029, "CLOSED", "VALIDATED", false), (described.EditionYear, described.EditionStatus, described.OrderStatus, described.Erased));
        var sheet = Assert.Single(parts.SelectMany(p => p.Sheets), s => s.Code == "entries");
        Assert.Equal(entry.NationalId, Assert.Single(sheet.Rows)[sheet.Columns.ToList().IndexOf("nationalId")]);
        Assert.Equal(1, result!.Counts["entriesAnonymised"]);
        var erased = await EntryAsync(entry.Id);
        Assert.Equal((null, null, null, null, 2), (erased.FirstName, erased.LastName, erased.NationalId, erased.FederationId, erased.PowderKg));
        Assert.NotNull(erased.ErasedAt);
        Assert.True((await RunAsync(r => r.DescribeAsync(person, TestContext.Current.CancellationToken))).Entries.Count == 0);
    }

    [Fact]
    public async Task An_entry_with_an_old_dni_is_found_through_the_registered_arquebusier()
    {
        var arquebusier = await _registry.RegisterAsync(_registry.Own.Id);
        var id = arquebusier.GetProperty("id").GetGuid();
        var order = await OrderAsync(2028, EditionStatus.Closed, OrderStatus.Validated);
        var entry = NewEntry(order, id); // its copy holds another (older) DNI
        await _registry.Services.SaveOrdersAsync(entry);

        var result = await RunAsync(r => r.EraseAsync(
            new PersonalDataSubject.Person(arquebusier.GetProperty("nationalId").GetString()!), Audit, TestContext.Current.CancellationToken));

        Assert.Equal(1, result!.Counts["entriesAnonymised"]);
        Assert.Null((await EntryAsync(entry.Id)).NationalId);
    }

    [Fact]
    public async Task An_external_lender_is_anonymised_and_the_model_kept()
    {
        var order = await OrderAsync(2030, EditionStatus.Closed, OrderStatus.Validated);
        var borrower = NewEntry(order, null);
        (borrower.WeaponSource, borrower.FirstName, borrower.LastName) = (WeaponSource.Loan, "Prestataria", "Sintética Receptora");
        var lenderId = RegistryData.NextIdentity().NationalId;
        var loan = new WeaponLoan
        {
            Id = Guid.CreateVersion7(),
            EntryId = borrower.Id,
            LenderKind = LenderKind.External,
            CopiedAt = DateTimeOffset.UtcNow,
            LenderFirstName = "Prestadora",
            LenderLastName = "Sintética Externa",
            LenderNationalId = lenderId,
            WeaponModelId = _model.Id,
            WeaponNumber = "55",
            OwnershipGuideNumber = "GUIA-EXTERNA-1",
        };
        await _registry.Services.SaveOrdersAsync(borrower, loan);
        var person = new PersonalDataSubject.Person(lenderId);

        var summary = await RunAsync(r => r.DescribeAsync(person, TestContext.Current.CancellationToken));
        var parts = await RunAsync(r => r.ExportAsync(person, TestContext.Current.CancellationToken));
        var result = await RunAsync(r => r.EraseAsync(person, Audit, TestContext.Current.CancellationToken));

        Assert.Equal(new LenderLoansSummary(2030, 1), Assert.Single(summary.LenderLoans));
        var sheet = Assert.Single(parts.SelectMany(p => p.Sheets));
        Assert.Equal("lenderLoans", sheet.Code);
        Assert.DoesNotContain(borrower.NationalId, sheet.Rows.SelectMany(r => r).OfType<string>());
        // The other party appears by role only: neither the borrower's DNI/NIE nor their names (SEC-06).
        Assert.DoesNotContain(sheet.Rows.SelectMany(r => r).OfType<string>(), value =>
            value.Contains(borrower.LastName!, StringComparison.Ordinal) || value.Contains(borrower.FirstName!, StringComparison.Ordinal));
        Assert.Equal(1, result!.Counts["loansAnonymised"]);
        var erased = await LoanAsync(loan.Id);
        Assert.Equal((null, null, null, null, _model.Id), (erased.LenderFirstName, erased.LenderNationalId, erased.WeaponNumber, erased.OwnershipGuideNumber, erased.WeaponModelId));
        Assert.NotNull(erased.ErasedAt);
        Assert.Equal(borrower.NationalId, (await EntryAsync(borrower.Id)).NationalId);
    }

    [Fact]
    public async Task A_registered_lender_loses_the_copy_of_their_lent_weapon()
    {
        var arquebusier = await _registry.RegisterAsync(_registry.Own.Id);
        var id = arquebusier.GetProperty("id").GetGuid();
        using (var added = await _registry.Admin.PostAsJsonAsync(
            $"/api/arquebusiers/{id}/owned-weapons",
            new { weaponModelId = _model.Id, weaponNumber = "12", ownershipGuideNumber = "GUIA-PRESTADA-1" },
            TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        }

        var weaponId = await WeaponOfAsync(id);
        var order = await OrderAsync(2030, EditionStatus.Closed, OrderStatus.Validated);
        var borrower = NewEntry(order, null);
        borrower.WeaponSource = WeaponSource.Loan;
        var loan = new WeaponLoan
        {
            Id = Guid.CreateVersion7(),
            EntryId = borrower.Id,
            LenderKind = LenderKind.Arquebusier,
            CopiedAt = DateTimeOffset.UtcNow,
            LenderOwnedWeaponId = weaponId,
            LenderComparsaId = _registry.Own.Id,
            LenderFirstName = "Arcabucera",
            LenderLastName = "Sintética Uno",
            LenderNationalId = "00000000T",
            WeaponModelId = _model.Id,
            WeaponNumber = "12",
            OwnershipGuideNumber = "GUIA-PRESTADA-1",
        };
        await _registry.Services.SaveOrdersAsync(borrower, loan);

        var result = await RunAsync(r => r.EraseAsync(
            new PersonalDataSubject.Person(arquebusier.GetProperty("nationalId").GetString()!), Audit, TestContext.Current.CancellationToken));

        Assert.Equal(1, result!.Counts["loansAnonymised"]);
        var erased = await LoanAsync(loan.Id);
        Assert.Equal((null, null, _registry.Own.Id), (erased.LenderNationalId, erased.OwnershipGuideNumber, erased.LenderComparsaId));
    }

    private static AuditRecord Audit(IReadOnlyDictionary<string, int> counts) =>
        new("PersonalDataErased", "PersonalDataRequest", Data: new { reference = "REQ-PRUEBA-2", counts });

    private async Task<T> RunAsync<T>(Func<PersonalDataRequests, Task<T>> run)
    {
        await using var scope = _registry.Services.CreateAsyncScope();
        return await run(scope.ServiceProvider.GetRequiredService<PersonalDataRequests>());
    }

    private async Task<PolvorApp.ComparsaOrders.Orders.ComparsaOrder> OrderAsync(int year, EditionStatus status, OrderStatus orderStatus)
    {
        var edition = NewEdition(year, status);
        await _registry.Services.SaveEditionsAsync(edition);
        var order = NewOrder(edition, _registry.Own.Id, orderStatus);
        await _registry.Services.SaveOrdersAsync(order);
        return order;
    }

    private async Task<EditionEntry> EntryAsync(Guid id)
    {
        await using var scope = _registry.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ComparsaOrdersDbContext>().Entries.AsNoTracking().SingleAsync(e => e.Id == id, TestContext.Current.CancellationToken);
    }

    private async Task<WeaponLoan> LoanAsync(Guid id)
    {
        await using var scope = _registry.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ComparsaOrdersDbContext>().Loans.AsNoTracking().SingleAsync(l => l.Id == id, TestContext.Current.CancellationToken);
    }

    private async Task<Guid> WeaponOfAsync(Guid arquebusierId)
    {
        await using var scope = _registry.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ArquebusierRegistryDbContext>().OwnedWeapons
            .Where(w => w.ArquebusierId == arquebusierId).Select(w => w.Id).SingleAsync(TestContext.Current.CancellationToken);
    }
}
