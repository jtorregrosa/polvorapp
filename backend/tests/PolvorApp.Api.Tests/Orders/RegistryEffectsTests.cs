using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ArquebusierRegistry.Persistence;
using PolvorApp.ComparsaOrders;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.ComparsaOrders.Persistence;
using PolvorApp.FestivalEditions.Persistence;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;
using static PolvorApp.Api.Tests.Infrastructure.OrderData;

namespace PolvorApp.Api.Tests.Orders;

/// <summary>
/// Specs "Entries after registry changes (BR-13, BR-14)" and "Deleting an arquebusier (UC-05, BR-14)"
/// (design D3): the entry of the edition in progress goes with the arquebusier while its orders are
/// open; every other entry stays as history; the deletion serialises with closings and order writes.
/// </summary>
public sealed class RegistryEffectsTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);
    private OrderTestHost _orders = null!;

    public async ValueTask InitializeAsync() => _orders = await OrderTestHost.StartAsync(postgres, mailpit);

    public async ValueTask DisposeAsync() => await _orders.DisposeAsync();

    [Theory]
    [InlineData(OrderStatus.Draft)]
    [InlineData(OrderStatus.Submitted)]
    [InlineData(OrderStatus.Validated)]
    public async Task With_open_orders_a_deletion_removes_the_current_entry_and_keeps_the_order_status(OrderStatus status)
    {
        var (leaving, _) = await _orders.AddArquebusierAsync(_orders.Own.Id, "Se Va Sintético");
        await _orders.AddArquebusierAsync(_orders.Own.Id, "Se Queda Sintético");
        var order = await _orders.PrepareAsync(_orders.Admin, _orders.Own.Id);
        await SetStatusAsync(Id(order), status);
        var before = await OrderTestHost.GetOrderAsync(_orders.Admin, Id(order));

        await DeleteAsync(leaving.Id);

        var after = await OrderTestHost.GetOrderAsync(_orders.Admin, Id(order));
        Assert.Equal(["Se Queda Sintético"], after.GetProperty("entries").EnumerateArray().Select(e => e.GetProperty("arquebusier").GetProperty("lastName").GetString()));
        Assert.Equal(before.GetProperty("status").GetString(), after.GetProperty("status").GetString());
        Assert.NotEqual(before.GetProperty("version").GetUInt32(), after.GetProperty("version").GetUInt32());
        var audit = Assert.Single(await _orders.Host.AuditEntriesAsync("ArquebusierDeleted"));
        using var data = JsonDocument.Parse(audit.Data!);
        Assert.Equal([Id(order).ToString()], data.RootElement.GetProperty("orderIds").EnumerateArray().Select(o => o.GetString()));
    }

    [Fact]
    public async Task With_closed_orders_the_entry_stays_as_history_and_an_Admin_can_still_edit_it()
    {
        var (leaving, _) = await _orders.AddArquebusierAsync(_orders.Own.Id, "Histórico Sintético");
        var order = await _orders.PrepareAsync(_orders.Admin, _orders.Own.Id);
        await _orders.SetOrdersOpenAsync(false);

        await DeleteAsync(leaving.Id);

        var after = await OrderTestHost.GetOrderAsync(_orders.Admin, Id(order));
        var entry = Assert.Single(after.GetProperty("entries").EnumerateArray());
        var person = entry.GetProperty("arquebusier");
        Assert.Equal(
            ("Histórico Sintético", leaving.NationalId, leaving.FederationId, false),
            (person.GetProperty("lastName").GetString(), person.GetProperty("nationalId").GetString(),
             person.GetProperty("federationId").GetInt32(), person.GetProperty("inRegistry").GetBoolean()));
        Assert.Empty(entry.GetProperty("warnings").EnumerateArray());
        using var edit = await _orders.Admin.PutAsJsonAsync(
            $"/api/comparsa-orders/{Id(order)}/entries/{entry.GetProperty("id").GetString()}",
            new { version = entry.GetProperty("version").GetUInt32(), status = "RESERVE", powderKg = 0, capsBoxes = 0, weaponSource = "NONE", flask = "NONE" },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
    }

    [Fact]
    public async Task Entries_of_past_editions_keep_their_copy()
    {
        var (leaving, _) = await _orders.AddArquebusierAsync(_orders.Own.Id, "Veterano Sintético");
        var past = NewOrder(_orders.Previous, _orders.Own.Id, OrderStatus.Validated);
        var entry = NewEntry(past, leaving.Id);
        (entry.LastName, entry.PowderKg) = (leaving.LastName, 2);
        await _orders.Services.SaveOrdersAsync(past, entry);

        await DeleteAsync(leaving.Id);

        var kept = await _orders.ReadOrdersAsync(db => db.Entries.AsNoTracking().SingleAsync(e => e.Id == entry.Id, TestContext.Current.CancellationToken));
        Assert.Equal(((Guid?)null, "Veterano Sintético", 2), (kept.ArquebusierId, kept.LastName, kept.PowderKg));
    }

    [Fact]
    public async Task A_deleted_lenders_weapon_shows_as_removed_in_the_borrowers_loan_with_its_copy()
    {
        var (lender, weapons) = await _orders.AddArquebusierAsync(_orders.Other.Id, "Prestamista Ido Sintético", weapons: 1);
        var (borrower, _) = await _orders.AddArquebusierAsync(_orders.Own.Id, "Prestatario Sintético");
        var order = await _orders.PrepareAsync(_orders.FiringChief, _orders.Own.Id);
        var entry = order.GetProperty("entries")[0];
        using (var lend = await _orders.FiringChief.PutAsJsonAsync(
            $"/api/comparsa-orders/{Id(order)}/entries/{entry.GetProperty("id").GetString()}",
            new { version = entry.GetProperty("version").GetUInt32(), status = "ACTIVE", powderKg = 1, capsBoxes = 0, weaponSource = "LOAN", loan = new { ownedWeaponId = weapons[0].Id }, flask = "NONE" },
            TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, lend.StatusCode);
        }

        await DeleteAsync(lender.Id);

        var after = await OrderTestHost.GetOrderAsync(_orders.FiringChief, Id(order));
        var loan = after.GetProperty("entries")[0].GetProperty("loan");
        Assert.Equal((true, weapons[0].WeaponNumber, "Prestamista Ido Sintético"), (loan.GetProperty("weaponRemoved").GetBoolean(), loan.GetProperty("weaponNumber").GetString(), loan.GetProperty("lenderLastName").GetString()));
        Assert.Equal(["loanWeaponMissing"], after.GetProperty("entries")[0].GetProperty("issues").EnumerateArray().Select(i => i.GetString()));
    }

    [Fact]
    public async Task A_registry_status_change_leaves_the_entries_unchanged()
    {
        var (arquebusier, _) = await _orders.AddArquebusierAsync(_orders.Own.Id, "Cambia Estado Sintético");
        await _orders.PrepareAsync(_orders.FiringChief, _orders.Own.Id);
        await using (var scope = _orders.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ArquebusierRegistryDbContext>().Arquebusiers.Where(a => a.Id == arquebusier.Id)
                .ExecuteUpdateAsync(a => a.SetProperty(x => x.Status, ArquebusierStatus.Reserve), TestContext.Current.CancellationToken);
        }

        var entry = await _orders.ReadOrdersAsync(db => db.Entries.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken));

        Assert.Equal(ArquebusierStatus.Active, entry.Status);
    }

    [Fact]
    public async Task A_deletion_that_races_with_the_closing_of_the_orders_waits_and_keeps_the_entry()
    {
        var (leaving, _) = await _orders.AddArquebusierAsync(_orders.Own.Id, "Carrera Cierre Sintético");
        await _orders.PrepareAsync(_orders.FiringChief, _orders.Own.Id);

        await using var scope = _orders.Services.CreateAsyncScope();
        var editions = scope.ServiceProvider.GetRequiredService<FestivalEditionsDbContext>();
        Task<HttpResponseMessage> deleting;
        await using (var closing = await editions.Database.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            await editions.Database.ExecuteSqlAsync(
                $"UPDATE editions.festival_editions SET orders_open = false WHERE id = {_orders.Current.Id}", TestContext.Current.CancellationToken);
            deleting = _orders.Admin.DeleteAsync($"/api/arquebusiers/{leaving.Id}", TestContext.Current.CancellationToken);
            await WaitUntilBlockedOnAsync("festival_editions");
            await closing.CommitAsync(TestContext.Current.CancellationToken);
        }

        using var deleted = await deleting;
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(1, await _orders.ReadOrdersAsync(db => db.Entries.CountAsync(TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task A_deletion_that_races_with_an_order_write_waits_for_the_order_lock()
    {
        var (leaving, _) = await _orders.AddArquebusierAsync(_orders.Own.Id, "Carrera Pedido Sintético");
        var order = await _orders.PrepareAsync(_orders.FiringChief, _orders.Own.Id);

        await using var scope = _orders.Services.CreateAsyncScope();
        var orders = scope.ServiceProvider.GetRequiredService<ComparsaOrdersDbContext>();
        Task<HttpResponseMessage> deleting;
        await using (var writer = await orders.Database.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            await orders.Database.SqlQuery<int>($"SELECT 1 AS \"Value\" FROM orders.comparsa_orders WHERE id = {Id(order)} FOR UPDATE")
                .ToListAsync(TestContext.Current.CancellationToken);
            deleting = _orders.Admin.DeleteAsync($"/api/arquebusiers/{leaving.Id}", TestContext.Current.CancellationToken);
            await WaitUntilBlockedOnAsync("comparsa_orders");
            await writer.CommitAsync(TestContext.Current.CancellationToken);
        }

        using var deleted = await deleting;
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(0, await _orders.ReadOrdersAsync(db => db.Entries.CountAsync(TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task The_remaining_lock_cycle_ends_retryable_on_one_side_and_commits_on_the_other()
    {
        var (leaving, _) = await _orders.AddArquebusierAsync(_orders.Own.Id, "Ciclo Sintético");
        var order = await _orders.PrepareAsync(_orders.FiringChief, _orders.Own.Id);

        await using var scope = _orders.Services.CreateAsyncScope();
        var orders = scope.ServiceProvider.GetRequiredService<ComparsaOrdersDbContext>();
        var writer = await orders.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await orders.Database.SqlQuery<int>($"SELECT 1 AS \"Value\" FROM orders.comparsa_orders WHERE id = {Id(order)} FOR UPDATE")
            .ToListAsync(TestContext.Current.CancellationToken);
        var deleting = _orders.Admin.DeleteAsync($"/api/arquebusiers/{leaving.Id}", TestContext.Current.CancellationToken);
        await WaitUntilBlockedOnAsync("comparsa_orders");

        // The writer now needs the arquebusier, as a foreign key check would: a cycle.
        PostgresException? writerError = null;
        try
        {
            await orders.Database.SqlQuery<int>($"SELECT 1 AS \"Value\" FROM registry.arquebusiers WHERE id = {leaving.Id} FOR KEY SHARE")
                .ToListAsync(TestContext.Current.CancellationToken);
            await writer.CommitAsync(TestContext.Current.CancellationToken);
        }
        catch (PostgresException exception)
        {
            writerError = exception;
        }
        finally
        {
            await writer.DisposeAsync();
        }

        using var deleted = await deleting;
        if (writerError is null)
        {
            await AssertProblemAsync(deleted, HttpStatusCode.ServiceUnavailable, "registry.busy");
        }
        else
        {
            Assert.Equal(PostgresErrorCodes.DeadlockDetected, writerError.SqlState);
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        }
    }

    [Fact]
    public async Task A_write_that_loses_a_race_with_a_registry_deletion_is_answered_as_modified()
    {
        var order = await _orders.PrepareAsync(_orders.Admin, _orders.Own.Id);
        await using var scope = _orders.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ComparsaOrdersDbContext>();
        var guard = scope.ServiceProvider.GetRequiredService<OrderWriteGuard>();
        db.Entries.Add(NewEntry(await db.Orders.SingleAsync(o => o.Id == Id(order), TestContext.Current.CancellationToken), Guid.CreateVersion7()));

        var outcome = await db.SaveAsync(guard, Id(order), OrderOutcome.AlreadyInEdition, TestContext.Current.CancellationToken);

        Assert.Equal(OrderOutcome.Modified, outcome);
    }

    private async Task DeleteAsync(Guid arquebusierId)
    {
        using var response = await _orders.Admin.DeleteAsync($"/api/arquebusiers/{arquebusierId}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private async Task SetStatusAsync(Guid orderId, OrderStatus status)
    {
        await using var scope = _orders.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ComparsaOrdersDbContext>().Orders.Where(o => o.Id == orderId)
            .ExecuteUpdateAsync(o => o.SetProperty(x => x.Status, status), TestContext.Current.CancellationToken);
    }

    private static Guid Id(JsonElement order) => Guid.Parse(order.GetProperty("id").GetString()!);

    /// <summary>Waits until some backend is waiting on a lock in a statement on <paramref name="table"/>.</summary>
    private async Task WaitUntilBlockedOnAsync(string table)
    {
        var dataSource = _orders.Services.GetRequiredService<NpgsqlDataSource>();
        var deadline = DateTimeOffset.UtcNow + WaitLimit;
        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var command = dataSource.CreateCommand(
                "SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock' AND query LIKE $1");
            command.Parameters.Add(new NpgsqlParameter { Value = "%" + table + "%" });
            if ((long)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))! > 0)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken);
        }

        Assert.Fail($"No statement on {table} waited on a lock within {WaitLimit}.");
    }
}
