using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.ArquebusierRegistry.Persistence;
using PolvorApp.AuditPrivacy.Contracts;
using PolvorApp.AuditPrivacy.Privacy;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.ComparsaOrders.Loans;
using PolvorApp.ComparsaOrders.Persistence;
using PolvorApp.FederationCatalog.WeaponModels;
using PolvorApp.FestivalEditions.Contracts;
using PolvorApp.IdentityAccess.Persistence;
using PolvorApp.SharedKernel.Auditing;
using static PolvorApp.Api.Tests.Infrastructure.OrderData;

namespace PolvorApp.Api.Tests.Privacy;

/// <summary>
/// Spec audit-privacy "Erasing a person's data" (atomic, edition in progress) and "Erasing a user's data"
/// (last Admin, credentials), design D6–D8, and the group 4 review findings.
/// </summary>
public sealed class ErasureIntegrityTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private readonly WeaponModel _model = RegistryData.NewWeaponModel("ARCABUZ SINTÉTICO ÍNTEGRO");
    private readonly FailingParticipant _failing = new();
    private RegistryTestHost _registry = null!;

    public async ValueTask InitializeAsync()
    {
        _registry = await RegistryTestHost.StartAsync(
            postgres, mailpit, services => services.AddSingleton<IPersonalDataParticipant>(_failing));
        await _registry.Services.SaveCatalogAsync(_model);
    }

    public async ValueTask DisposeAsync() => await _registry.DisposeAsync();

    [Fact]
    public async Task A_failing_participant_rolls_the_whole_erasure_back()
    {
        var arquebusier = await _registry.RegisterAsync(_registry.Own.Id);
        var id = arquebusier.GetProperty("id").GetGuid();
        var order = await OrderAsync(2029, EditionStatus.Closed);
        var entry = NewEntry(order, id);
        await _registry.Services.SaveOrdersAsync(entry);
        _failing.Fail = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => EraseAsync(arquebusier.GetProperty("nationalId").GetString()!));

        using var still = await _registry.Admin.GetAsync($"/api/arquebusiers/{id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, still.StatusCode);
        Assert.NotNull((await EntryAsync(entry.Id)).NationalId);
        Assert.Empty(await _registry.Host.AuditEntriesAsync("PersonalDataErased"));
        Assert.Empty(await _registry.Host.AuditEntriesAsync("ArquebusierDeleted"));
    }

    [Fact]
    public async Task An_erasure_whose_audit_cannot_be_recorded_changes_nothing()
    {
        var arquebusier = await _registry.RegisterAsync(_registry.Own.Id);
        var id = arquebusier.GetProperty("id").GetGuid();
        var order = await OrderAsync(2028, EditionStatus.Closed);
        var entry = NewEntry(order, id);
        await _registry.Services.SaveOrdersAsync(entry);

        // A code missing from the catalogue makes the audit trail refuse the record, inside the transaction.
        await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(r => r.EraseAsync(
            new PersonalDataSubject.Person(arquebusier.GetProperty("nationalId").GetString()!),
            counts => new AuditRecord("UndeclaredErasure", "PersonalDataRequest", Data: new { counts }),
            TestContext.Current.CancellationToken)));

        using var still = await _registry.Admin.GetAsync($"/api/arquebusiers/{id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, still.StatusCode);
        var kept = await EntryAsync(entry.Id);
        Assert.Equal((id, (DateTimeOffset?)null), (kept.ArquebusierId!.Value, kept.ErasedAt));
        Assert.NotNull(kept.NationalId);
        Assert.Empty(await _registry.Host.AuditEntriesAsync("ArquebusierDeleted"));
    }

    [Fact]
    public async Task With_the_orders_open_the_current_entry_is_removed_and_with_them_closed_it_is_anonymised()
    {
        var open = await _registry.RegisterAsync(_registry.Own.Id);
        var closed = await _registry.RegisterAsync(_registry.Own.Id);
        var edition = NewEdition(2031, EditionStatus.InProgress, ordersOpen: true);
        await _registry.Services.SaveEditionsAsync(edition);
        var order = NewOrder(edition, _registry.Own.Id);
        var openEntry = NewEntry(order, open.GetProperty("id").GetGuid());
        await _registry.Services.SaveOrdersAsync(order, openEntry);

        var summary = await RunAsync(r => r.DescribeAsync(
            new PersonalDataSubject.Person(open.GetProperty("nationalId").GetString()!), TestContext.Current.CancellationToken));
        await EraseAsync(open.GetProperty("nationalId").GetString()!);

        Assert.True(Assert.Single(summary.Entries).OrdersOpen);
        Assert.False(await EntryExistsAsync(openEntry.Id));

        await SetOrdersOpenAsync(edition.Id, false);
        var closedEntry = NewEntry(order, closed.GetProperty("id").GetGuid());
        await _registry.Services.SaveOrdersAsync(closedEntry);
        await EraseAsync(closed.GetProperty("nationalId").GetString()!);

        var kept = await EntryAsync(closedEntry.Id);
        Assert.Null(kept.NationalId);
        Assert.NotNull(kept.ErasedAt);
    }

    [Fact]
    public async Task A_lender_whose_dni_changed_still_sees_their_loan_in_the_lookup()
    {
        var lender = await _registry.RegisterAsync(_registry.Own.Id);
        var lenderId = lender.GetProperty("id").GetGuid();
        using (var added = await _registry.Admin.PostAsJsonAsync(
            $"/api/arquebusiers/{lenderId}/owned-weapons",
            new { weaponModelId = _model.Id, weaponNumber = "31", ownershipGuideNumber = "GUIA-CAMBIADA-1" },
            TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        }

        Guid weaponId;
        await using (var scope = _registry.Services.CreateAsyncScope())
        {
            weaponId = await scope.ServiceProvider.GetRequiredService<ArquebusierRegistryDbContext>().OwnedWeapons
                .Where(w => w.ArquebusierId == lenderId).Select(w => w.Id).SingleAsync(TestContext.Current.CancellationToken);
        }

        var order = await OrderAsync(2027, EditionStatus.Closed);
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
            LenderNationalId = RegistryData.NextIdentity().NationalId, // the DNI/NIE before a correction
            WeaponModelId = _model.Id,
            WeaponNumber = "31",
            OwnershipGuideNumber = "GUIA-CAMBIADA-1",
        };
        await _registry.Services.SaveOrdersAsync(borrower, loan);

        var summary = await RunAsync(r => r.DescribeAsync(
            new PersonalDataSubject.Person(lender.GetProperty("nationalId").GetString()!), TestContext.Current.CancellationToken));

        Assert.Equal(new LenderLoansSummary(2027, 1), Assert.Single(summary.LenderLoans));
    }

    [Fact]
    public async Task Two_erasures_run_one_after_the_other_in_one_scope()
    {
        var first = await _registry.RegisterAsync(_registry.Own.Id);
        var second = await _registry.RegisterAsync(_registry.Own.Id);
        await using var scope = _registry.Services.CreateAsyncScope();
        var requests = scope.ServiceProvider.GetRequiredService<PersonalDataRequests>();

        var a = await requests.EraseAsync(new PersonalDataSubject.Person(first.GetProperty("nationalId").GetString()!), Audit, TestContext.Current.CancellationToken);
        var b = await requests.EraseAsync(new PersonalDataSubject.Person(second.GetProperty("nationalId").GetString()!), Audit, TestContext.Current.CancellationToken);

        Assert.Equal((1, 1), (a!.Counts["arquebusiersDeleted"], b!.Counts["arquebusiersDeleted"]));
    }

    [Fact]
    public async Task Two_erasures_of_the_same_person_at_once_erase_once()
    {
        var person = await _registry.RegisterAsync(_registry.Own.Id);
        var nationalId = person.GetProperty("nationalId").GetString()!;

        var results = await Task.WhenAll(EraseAsync(nationalId), EraseAsync(nationalId));

        // The second waits for the first's locks, then finds nothing left (404 at the endpoint).
        Assert.Single(results, result => result is not null);
        Assert.Single(await _registry.Host.AuditEntriesAsync("ArquebusierDeleted"));
    }

    [Fact]
    public async Task An_erased_user_has_no_credentials_and_cannot_be_assigned_again()
    {
        var chief = await _registry.Host.CreateUserAsync("jefa.sin.credenciales@example.test");

        await RunAsync(r => r.EraseAsync(new PersonalDataSubject.UserAccount(chief.Id), Audit, TestContext.Current.CancellationToken));

        await using (var scope = _registry.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityAccessDbContext>();
            var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == chief.Id, TestContext.Current.CancellationToken);
            Assert.Equal((null, false, false, null), (user.PasswordHash, user.TwoFactorEnabled, user.Active, user.LastSignInAt));
            Assert.Equal(0, await db.Database.SqlQuery<int>(
                $"SELECT count(*)::int AS \"Value\" FROM identity.user_tokens WHERE user_id = {chief.Id}").SingleAsync(TestContext.Current.CancellationToken));
        }

        using var assign = await _registry.Admin.PutAsync($"/api/comparsas/{_registry.Own.Id}/firing-chiefs/{chief.Id}", null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, assign.StatusCode);
        Assert.Empty(await _registry.Host.AssignedComparsasAsync(chief.Id));
    }

    private static AuditRecord Audit(IReadOnlyDictionary<string, int> counts) =>
        new("PersonalDataErased", "PersonalDataRequest", Data: new { reference = "REQ-PRUEBA-5", counts });

    private Task<ErasureResult?> EraseAsync(string nationalId) =>
        RunAsync(r => r.EraseAsync(new PersonalDataSubject.Person(nationalId), Audit, TestContext.Current.CancellationToken));

    private async Task<T> RunAsync<T>(Func<PersonalDataRequests, Task<T>> run)
    {
        await using var scope = _registry.Services.CreateAsyncScope();
        return await run(scope.ServiceProvider.GetRequiredService<PersonalDataRequests>());
    }

    private async Task<PolvorApp.ComparsaOrders.Orders.ComparsaOrder> OrderAsync(int year, EditionStatus status)
    {
        var edition = NewEdition(year, status);
        await _registry.Services.SaveEditionsAsync(edition);
        var order = NewOrder(edition, _registry.Own.Id, OrderStatus.Validated);
        await _registry.Services.SaveOrdersAsync(order);
        return order;
    }

    private async Task SetOrdersOpenAsync(Guid editionId, bool open)
    {
        await using var scope = _registry.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<PolvorApp.FestivalEditions.Persistence.FestivalEditionsDbContext>().Editions
            .Where(e => e.Id == editionId).ExecuteUpdateAsync(e => e.SetProperty(x => x.OrdersOpen, open), TestContext.Current.CancellationToken);
    }

    private async Task<PolvorApp.ComparsaOrders.Entries.EditionEntry> EntryAsync(Guid id)
    {
        await using var scope = _registry.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ComparsaOrdersDbContext>().Entries.AsNoTracking().SingleAsync(e => e.Id == id, TestContext.Current.CancellationToken);
    }

    private async Task<bool> EntryExistsAsync(Guid id)
    {
        await using var scope = _registry.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ComparsaOrdersDbContext>().Entries.AnyAsync(e => e.Id == id, TestContext.Current.CancellationToken);
    }

    /// <summary>Runs after the orders participant and fails when asked, to prove the erasure is atomic.</summary>
    private sealed class FailingParticipant : IPersonalDataParticipant
    {
        public bool Fail { get; set; }

        public int Order => PersonalDataParticipantOrder.Orders + 5;

        public Task<PersonalDataSummary> DescribeAsync(PersonalDataSubject subject, CancellationToken cancellationToken) =>
            Task.FromResult(PersonalDataSummary.Empty);

        public Task<PersonalDataExportPart> ExportAsync(PersonalDataSubject subject, CancellationToken cancellationToken) =>
            Task.FromResult(PersonalDataExportPart.Empty);

        public Task PrepareErasureAsync(PersonalDataSubject subject, PersonalDataErasure erasure, DbTransaction transaction, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task EraseAsync(PersonalDataSubject subject, PersonalDataErasure erasure, DbTransaction transaction, CancellationToken cancellationToken) =>
            Fail ? throw new InvalidOperationException("Synthetic failure after the registry and orders erased.") : Task.CompletedTask;
    }
}

/// <summary>Spec audit-privacy "Erasing a user's data": the last active Admin is protected.</summary>
public sealed class LastAdminErasureTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private IdentityTestHost _host = null!;

    public async ValueTask InitializeAsync() => _host = await IdentityTestHost.StartAsync(postgres, mailpit);

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task The_only_active_admin_cannot_be_erased()
    {
        var admin = await _host.CreateUserAsync("unica.admin@example.test", PolvorApp.IdentityAccess.Contracts.UserRole.Admin);
        await using var scope = _host.Services.CreateAsyncScope();
        var requests = scope.ServiceProvider.GetRequiredService<PersonalDataRequests>();

        var refused = await Assert.ThrowsAsync<PersonalDataErasureRefusedException>(() => requests.EraseAsync(
            new PersonalDataSubject.UserAccount(admin.Id),
            counts => new AuditRecord("PersonalDataErased", "PersonalDataRequest", Data: new { counts }),
            TestContext.Current.CancellationToken));

        Assert.Equal(PersonalDataErasureRefusedException.LastAdmin, refused.Code);
    }

    [Fact]
    public async Task Two_admins_erasing_each_other_at_once_leave_one_active_admin()
    {
        var first = await _host.CreateUserAsync("primera.admin@example.test", PolvorApp.IdentityAccess.Contracts.UserRole.Admin);
        var second = await _host.CreateUserAsync("segunda.admin@example.test", PolvorApp.IdentityAccess.Contracts.UserRole.Admin);

        var outcomes = await Task.WhenAll(EraseAsync(first.Id), EraseAsync(second.Id));

        Assert.Equal(1, outcomes.Count(refused => refused is null));
        Assert.Equal(PersonalDataErasureRefusedException.LastAdmin, Assert.Single(outcomes, refused => refused is not null));
        await using var scope = _host.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<IdentityAccessDbContext>().Users.AsNoTracking();
        Assert.Equal(1, await users.CountAsync(
            u => (u.Id == first.Id || u.Id == second.Id) && u.Active && u.ErasedAt == null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_user_erasure_whose_audit_cannot_be_recorded_changes_nothing()
    {
        var chief = await _host.CreateUserAsync("jefa.intacta@example.test", PolvorApp.IdentityAccess.Contracts.UserRole.FiringChief);
        await using var scope = _host.Services.CreateAsyncScope();

        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<PersonalDataRequests>().EraseAsync(
            new PersonalDataSubject.UserAccount(chief.Id),
            counts => new AuditRecord("UndeclaredErasure", "PersonalDataRequest", Data: new { counts }),
            TestContext.Current.CancellationToken));

        await using var read = _host.Services.CreateAsyncScope();
        var user = await read.ServiceProvider.GetRequiredService<IdentityAccessDbContext>().Users.AsNoTracking()
            .SingleAsync(u => u.Id == chief.Id, TestContext.Current.CancellationToken);
        Assert.Equal(("jefa.intacta@example.test", (DateTimeOffset?)null), (user.Email, user.ErasedAt));
        Assert.NotNull(user.PasswordHash);
    }

    /// <summary>Erases a user in its own scope; the refusal code, or null when it was erased.</summary>
    private async Task<string?> EraseAsync(Guid userId)
    {
        await using var scope = _host.Services.CreateAsyncScope();
        try
        {
            await scope.ServiceProvider.GetRequiredService<PersonalDataRequests>().EraseAsync(
                new PersonalDataSubject.UserAccount(userId),
                counts => new AuditRecord("PersonalDataErased", "PersonalDataRequest", Data: new { counts }),
                TestContext.Current.CancellationToken);
            return null;
        }
        catch (PersonalDataErasureRefusedException refused)
        {
            return refused.Code;
        }
    }
}
