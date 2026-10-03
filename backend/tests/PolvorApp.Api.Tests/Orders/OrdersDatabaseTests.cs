using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PolvorApp.Api.Platform.Database;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ArquebusierRegistry.Persistence;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.ComparsaOrders.Entries;
using PolvorApp.ComparsaOrders.Loans;
using PolvorApp.ComparsaOrders.Orders;
using PolvorApp.ComparsaOrders.Persistence;
using PolvorApp.FederationCatalog.Persistence;
using PolvorApp.FestivalEditions.Contracts;
using PolvorApp.FestivalEditions.Editions;
using PolvorApp.FestivalEditions.Persistence;
using static PolvorApp.Api.Tests.Infrastructure.OrderData;
using static PolvorApp.Api.Tests.Infrastructure.RegistryData;

namespace PolvorApp.Api.Tests.Orders;

/// <summary>
/// Design D2 and D3: the database backs up the blocking order rules, and its keys to the registry
/// let entries outlive the arquebusier and their weapons with their copy.
/// </summary>
public sealed class OrdersDatabaseTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiFactory? _factory;

    private IServiceProvider Services => _factory!.Services;

    /// <summary>Mutations of a valid <c>ACTIVE</c> entry, each breaking one check (design D2).</summary>
    public static TheoryData<string, string> InvalidEntries => new()
    {
        { "reserve with powder", ComparsaOrdersDbContext.ReserveCheck },
        { "three kilograms", ComparsaOrdersDbContext.PowderCheck },
        { "a hundred caps boxes", ComparsaOrdersDbContext.CapsBoxesCheck },
        { "caps without a type", ComparsaOrdersDbContext.CapsTypeCheck },
        { "a caps type without boxes", ComparsaOrdersDbContext.CapsTypeCheck },
        { "a rental without a model", ComparsaOrdersDbContext.RentalCheck },
        { "a model without a rental", ComparsaOrdersDbContext.RentalCheck },
        { "an owned weapon on another source", ComparsaOrdersDbContext.OwnedCheck },
        { "an owned weapon copy on another source", ComparsaOrdersDbContext.OwnedCheck },
        { "a linked entry without its copy", ComparsaOrdersDbContext.CopyCheck },
    };

    public async ValueTask InitializeAsync()
    {
        _factory = new ApiFactory(await postgres.CreateMigratedDatabaseAsync());
        Assert.Equal(0, await MigrateCommand.RunAsync(_factory.Services, TestContext.Current.CancellationToken));
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
    }

    [Fact]
    public async Task A_second_order_for_the_same_comparsa_and_edition_violates_the_index()
    {
        var (edition, comparsa) = await EditionAndComparsaAsync();
        await Services.SaveOrdersAsync(NewOrder(edition, comparsa));

        var error = await FailAsync(NewOrder(edition, comparsa));

        Assert.Equal((PostgresErrorCodes.UniqueViolation, ComparsaOrdersDbContext.OrderIndex), error);
    }

    [Fact]
    public async Task A_second_entry_for_one_arquebusier_in_one_edition_violates_the_partial_index()
    {
        var (edition, comparsa) = await EditionAndComparsaAsync();
        var other = NewComparsa("Comparsa Sintética Índice");
        await Services.SaveCatalogAsync(other);
        var arquebusier = NewArquebusier(comparsa);
        await Services.SaveRegistryAsync(arquebusier);
        var first = NewOrder(edition, comparsa);
        var second = NewOrder(edition, other.Id);
        await Services.SaveOrdersAsync(first, second, NewEntry(first, arquebusier.Id));

        var error = await FailAsync(NewEntry(second, arquebusier.Id));

        Assert.Equal((PostgresErrorCodes.UniqueViolation, ComparsaOrdersDbContext.EntryIndex), error);
    }

    [Fact]
    public async Task Entries_no_longer_linked_to_the_registry_do_not_collide()
    {
        var (edition, comparsa) = await EditionAndComparsaAsync();
        var order = NewOrder(edition, comparsa);

        await Services.SaveOrdersAsync(order, NewEntry(order, null), NewEntry(order, null));

        Assert.Equal(2, await CountEntriesAsync());
    }

    [Fact]
    public async Task An_entry_in_another_edition_than_its_order_violates_the_composite_key()
    {
        var order = await OrderAsync();
        var entry = NewEntry(order, null);
        entry = CopyWithEdition(entry, Guid.CreateVersion7());

        var (sqlState, _) = await FailAsync(entry);

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, sqlState);
    }

    [Theory]
    [MemberData(nameof(InvalidEntries))]
    public async Task An_entry_breaking_a_rule_violates_its_check(string mutation, string constraint)
    {
        var order = await OrderAsync();
        var entry = NewEntry(order, null);
        Mutate(entry, mutation);

        Assert.Equal((PostgresErrorCodes.CheckViolation, constraint), await FailAsync(entry));
    }

    [Fact]
    public async Task The_allowed_shapes_are_stored()
    {
        var model = NewWeaponModel("ARCABUZ SINTÉTICO FORMAS");
        await Services.SaveCatalogAsync(model);
        var order = await OrderAsync();
        var reserve = NewEntry(order, null, ArquebusierStatus.Reserve);
        var removedWeapon = NewEntry(order, null);
        (removedWeapon.WeaponSource, removedWeapon.OwnedWeaponModelId, removedWeapon.OwnedWeaponNumber, removedWeapon.OwnedWeaponGuideNumber) =
            (WeaponSource.Owned, model.Id, "1234", "GUIA-RETIRADA");
        var carrier = NewEntry(order, null);
        (carrier.PowderKg, carrier.CapsBoxes, carrier.CapsType, carrier.Flask) = (2, 3, CapsType.Small, FlaskOption.Rental2Kg);
        var borrower = NewEntry(order, null);
        borrower.WeaponSource = WeaponSource.Loan;
        var removedLoan = new WeaponLoan
        {
            Id = Guid.CreateVersion7(),
            EntryId = borrower.Id,
            LenderKind = LenderKind.Arquebusier,
            LenderComparsaId = order.ComparsaId,
            WeaponModelId = model.Id,
            WeaponNumber = "5678",
            CopiedAt = DateTimeOffset.UtcNow,
        };

        await Services.SaveOrdersAsync(reserve, removedWeapon, carrier, borrower, removedLoan);

        Assert.Equal(4, await CountEntriesAsync());
    }

    [Fact]
    public async Task An_external_loan_naming_a_registered_weapon_or_comparsa_violates_the_check()
    {
        var order = await OrderAsync();
        var entry = NewEntry(order, null);
        entry.WeaponSource = WeaponSource.Loan;
        await Services.SaveOrdersAsync(entry);

        var withComparsa = NewLoan(entry.Id, LenderKind.External);
        withComparsa.LenderComparsaId = order.ComparsaId;
        var withWeapon = NewLoan(entry.Id, LenderKind.External);
        withWeapon.LenderOwnedWeaponId = Guid.CreateVersion7();

        Assert.Equal((PostgresErrorCodes.CheckViolation, ComparsaOrdersDbContext.ExternalLoanCheck), await FailAsync(withComparsa));
        Assert.Equal((PostgresErrorCodes.CheckViolation, ComparsaOrdersDbContext.ExternalLoanCheck), await FailAsync(withWeapon));
    }

    [Fact]
    public async Task A_registered_loan_without_the_lender_comparsa_violates_the_check()
    {
        var order = await OrderAsync();
        var entry = NewEntry(order, null);
        entry.WeaponSource = WeaponSource.Loan;
        await Services.SaveOrdersAsync(entry);

        var error = await FailAsync(NewLoan(entry.Id, LenderKind.Arquebusier));

        Assert.Equal((PostgresErrorCodes.CheckViolation, ComparsaOrdersDbContext.RegisteredLoanCheck), error);
    }

    [Fact]
    public async Task A_return_reason_exists_exactly_while_the_order_is_returned()
    {
        var (edition, comparsa) = await EditionAndComparsaAsync();
        var returnedWithout = NewOrder(edition, comparsa, OrderStatus.Returned);
        var draftWith = NewOrder(edition, comparsa);
        draftWith.ReturnReason = "Motivo sintético";
        var blank = NewOrder(edition, comparsa, OrderStatus.Returned);
        blank.ReturnReason = "   ";

        Assert.Equal((PostgresErrorCodes.CheckViolation, ComparsaOrdersDbContext.ReturnReasonCheck), await FailAsync(returnedWithout));
        Assert.Equal((PostgresErrorCodes.CheckViolation, ComparsaOrdersDbContext.ReturnReasonCheck), await FailAsync(draftWith));
        Assert.Equal((PostgresErrorCodes.CheckViolation, ComparsaOrdersDbContext.ReturnReasonCheck), await FailAsync(blank));
    }

    [Fact]
    public async Task A_submission_attested_and_made_by_an_admin_violates_the_check()
    {
        var (edition, comparsa) = await EditionAndComparsaAsync();
        var order = NewOrder(edition, comparsa, OrderStatus.Submitted);
        (order.Attested, order.SubmittedByAdmin) = (true, true);

        Assert.Equal((PostgresErrorCodes.CheckViolation, ComparsaOrdersDbContext.SubmissionCheck), await FailAsync(order));
    }

    [Fact]
    public async Task Deleting_an_arquebusier_unlinks_their_entries_and_weapons_and_keeps_the_copy()
    {
        var (edition, comparsa) = await EditionAndComparsaAsync();
        var model = NewWeaponModel("ARCABUZ SINTÉTICO BORRADO");
        await Services.SaveCatalogAsync(model);
        var owner = NewArquebusier(comparsa);
        var weapon = NewOwnedWeapon(owner.Id, model.Id, "GUIA-SET-NULL");
        var borrower = NewArquebusier(comparsa);
        await Services.SaveRegistryAsync(owner, weapon, borrower);
        var order = NewOrder(edition, comparsa);
        var owned = OwnedEntry(order, owner.Id, weapon.Id, model.Id, "GUIA-SET-NULL");
        var borrowed = NewEntry(order, borrower.Id);
        borrowed.WeaponSource = WeaponSource.Loan;
        var loan = NewLoan(borrowed.Id, LenderKind.Arquebusier);
        (loan.LenderOwnedWeaponId, loan.LenderComparsaId, loan.WeaponModelId, loan.WeaponNumber) = (weapon.Id, comparsa, model.Id, "1234");
        await Services.SaveOrdersAsync(order, owned, borrowed, loan);

        await DeleteAsync<ArquebusierRegistryDbContext>("DELETE FROM registry.arquebusiers WHERE id = {0}", owner.Id);

        await using var check = Services.CreateAsyncScope();
        var db = check.ServiceProvider.GetRequiredService<ComparsaOrdersDbContext>();
        var keptOwned = await db.Entries.AsNoTracking().SingleAsync(e => e.Id == owned.Id, TestContext.Current.CancellationToken);
        var keptLoan = await db.Loans.AsNoTracking().SingleAsync(l => l.Id == loan.Id, TestContext.Current.CancellationToken);
        Assert.Null(keptOwned.ArquebusierId);
        Assert.Null(keptOwned.OwnedWeaponId);
        Assert.Equal((WeaponSource.Owned, "Sintético Copia", "1234", "GUIA-SET-NULL"), (keptOwned.WeaponSource, keptOwned.LastName, keptOwned.OwnedWeaponNumber, keptOwned.OwnedWeaponGuideNumber));
        Assert.Null(keptLoan.LenderOwnedWeaponId);
        Assert.Equal((model.Id, "1234"), (keptLoan.WeaponModelId, keptLoan.WeaponNumber));
    }

    [Fact]
    public async Task Removing_only_an_owned_weapon_unlinks_it_and_keeps_the_arquebusier_link()
    {
        var (edition, comparsa) = await EditionAndComparsaAsync();
        var model = NewWeaponModel("ARCABUZ SINTÉTICO VENDIDO");
        await Services.SaveCatalogAsync(model);
        var owner = NewArquebusier(comparsa);
        var weapon = NewOwnedWeapon(owner.Id, model.Id, "GUIA-VENDIDA");
        await Services.SaveRegistryAsync(owner, weapon);
        var order = NewOrder(edition, comparsa);
        var owned = OwnedEntry(order, owner.Id, weapon.Id, model.Id, "GUIA-VENDIDA");
        await Services.SaveOrdersAsync(order, owned);

        await DeleteAsync<ArquebusierRegistryDbContext>("DELETE FROM registry.owned_weapons WHERE id = {0}", weapon.Id);

        await using var check = Services.CreateAsyncScope();
        var kept = await check.ServiceProvider.GetRequiredService<ComparsaOrdersDbContext>().Entries.AsNoTracking()
            .SingleAsync(e => e.Id == owned.Id, TestContext.Current.CancellationToken);
        Assert.Equal((owner.Id, (Guid?)null, "GUIA-VENDIDA"), (kept.ArquebusierId, kept.OwnedWeaponId, kept.OwnedWeaponGuideNumber));
    }

    [Fact]
    public async Task Deleting_an_edition_with_orders_violates_the_foreign_key()
    {
        var order = await OrderAsync();

        var error = await DeleteFailsAsync<FestivalEditionsDbContext>("DELETE FROM editions.festival_editions WHERE id = {0}", order.EditionId);

        Assert.Equal((PostgresErrorCodes.ForeignKeyViolation, ComparsaOrdersDbContext.EditionForeignKey), error);
    }

    [Fact]
    public async Task Deleting_a_comparsa_with_orders_violates_the_foreign_key()
    {
        var order = await OrderAsync();

        var error = await DeleteFailsAsync<FederationCatalogDbContext>("DELETE FROM catalog.comparsas WHERE id = {0}", order.ComparsaId);

        Assert.Equal((PostgresErrorCodes.ForeignKeyViolation, ComparsaOrdersDbContext.ComparsaForeignKey), error);
    }

    [Fact]
    public async Task Deleting_a_rented_weapon_model_violates_the_foreign_key()
    {
        var model = NewWeaponModel("ARCABUZ SINTÉTICO ALQUILADO");
        await Services.SaveCatalogAsync(model);
        var order = await OrderAsync();
        var entry = NewEntry(order, null);
        (entry.WeaponSource, entry.RentalWeaponModelId) = (WeaponSource.Rental, model.Id);
        await Services.SaveOrdersAsync(entry);

        var error = await DeleteFailsAsync<FederationCatalogDbContext>("DELETE FROM catalog.weapon_models WHERE id = {0}", model.Id);

        Assert.Equal((PostgresErrorCodes.ForeignKeyViolation, ComparsaOrdersDbContext.RentalModelForeignKey), error);
    }

    [Theory]
    [InlineData(ComparsaOrdersDbContext.EditionForeignKey, "NO ACTION")]
    [InlineData(ComparsaOrdersDbContext.ComparsaForeignKey, "NO ACTION")]
    [InlineData(ComparsaOrdersDbContext.RentalModelForeignKey, "NO ACTION")]
    [InlineData(ComparsaOrdersDbContext.OwnedWeaponModelForeignKey, "NO ACTION")]
    [InlineData(ComparsaOrdersDbContext.LoanWeaponModelForeignKey, "NO ACTION")]
    [InlineData(ComparsaOrdersDbContext.LoanComparsaForeignKey, "NO ACTION")]
    [InlineData(ComparsaOrdersDbContext.ArquebusierForeignKey, "SET NULL")]
    [InlineData(ComparsaOrdersDbContext.OwnedWeaponForeignKey, "SET NULL")]
    [InlineData(ComparsaOrdersDbContext.LoanOwnedWeaponForeignKey, "SET NULL")]
    public async Task Every_cross_schema_key_exists_with_its_delete_rule(string constraint, string deleteRule)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ComparsaOrdersDbContext>();

        var rules = await db.Database
            .SqlQuery<string>($"SELECT delete_rule AS \"Value\" FROM information_schema.referential_constraints WHERE constraint_schema = 'orders' AND constraint_name = {constraint}")
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal([deleteRule], rules);
    }

    private static void Mutate(EditionEntry entry, string mutation)
    {
        switch (mutation)
        {
            case "reserve with powder":
                (entry.Status, entry.PowderKg) = (ArquebusierStatus.Reserve, 1);
                break;
            case "three kilograms":
                entry.PowderKg = 3;
                break;
            case "a hundred caps boxes":
                (entry.CapsBoxes, entry.CapsType) = (100, CapsType.Normal);
                break;
            case "caps without a type":
                entry.CapsBoxes = 2;
                break;
            case "a caps type without boxes":
                entry.CapsType = CapsType.Small;
                break;
            case "a rental without a model":
                entry.WeaponSource = WeaponSource.Rental;
                break;
            case "a model without a rental":
                entry.RentalWeaponModelId = Guid.CreateVersion7();
                break;
            case "an owned weapon on another source":
                entry.OwnedWeaponId = Guid.CreateVersion7();
                break;
            case "an owned weapon copy on another source":
                entry.OwnedWeaponNumber = "1234";
                break;
            case "a linked entry without its copy":
                (entry.ArquebusierId, entry.NationalId) = (Guid.CreateVersion7(), null);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation), mutation, "Unknown mutation.");
        }
    }

    private static EditionEntry OwnedEntry(ComparsaOrder order, Guid arquebusierId, Guid weaponId, Guid modelId, string guide)
    {
        var entry = NewEntry(order, arquebusierId);
        (entry.WeaponSource, entry.OwnedWeaponId, entry.OwnedWeaponModelId, entry.OwnedWeaponNumber, entry.OwnedWeaponGuideNumber) =
            (WeaponSource.Owned, weaponId, modelId, "1234", guide);
        return entry;
    }

    private static EditionEntry CopyWithEdition(EditionEntry entry, Guid editionId) => new()
    {
        Id = entry.Id,
        OrderId = entry.OrderId,
        EditionId = editionId,
        Status = entry.Status,
        CreatedAt = entry.CreatedAt,
        CopiedAt = entry.CopiedAt,
    };

    private static WeaponLoan NewLoan(Guid entryId, LenderKind kind) => new()
    {
        Id = Guid.CreateVersion7(),
        EntryId = entryId,
        LenderKind = kind,
        CopiedAt = DateTimeOffset.UtcNow,
    };

    /// <remarks>Single use per test: a second call would reuse the year.</remarks>
    private async Task<(FestivalEdition Edition, Guid Comparsa)> EditionAndComparsaAsync()
    {
        var comparsa = NewComparsa("Comparsa Sintética " + Guid.NewGuid().ToString("N")[..8]);
        await Services.SaveCatalogAsync(comparsa);
        var edition = NewEdition(2031, EditionStatus.Closed);
        await Services.SaveEditionsAsync(edition);
        return (edition, comparsa.Id);
    }

    private async Task<ComparsaOrder> OrderAsync()
    {
        var (edition, comparsa) = await EditionAndComparsaAsync();
        var order = NewOrder(edition, comparsa);
        await Services.SaveOrdersAsync(order);
        return order;
    }

    private async Task<int> CountEntriesAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ComparsaOrdersDbContext>().Entries.CountAsync(TestContext.Current.CancellationToken);
    }

    private async Task<(string SqlState, string? Constraint)> FailAsync(object entity)
    {
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => Services.SaveOrdersAsync(entity));
        var database = Assert.IsType<PostgresException>(error.InnerException);
        return (database.SqlState, database.ConstraintName);
    }

    private async Task DeleteAsync<TContext>(string sql, Guid id)
        where TContext : DbContext
    {
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<TContext>().Database.ExecuteSqlRawAsync(sql, [id], TestContext.Current.CancellationToken);
    }

    private async Task<(string SqlState, string? Constraint)> DeleteFailsAsync<TContext>(string sql, Guid id)
        where TContext : DbContext
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql, [id], TestContext.Current.CancellationToken));
        return (error.SqlState, error.ConstraintName);
    }
}
