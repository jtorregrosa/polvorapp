using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.ComparsaOrders.Persistence;
using PolvorApp.FestivalEditions.Contracts;
using PolvorApp.FestivalEditions.Persistence;
using PolvorApp.IdentityAccess.Contracts;

namespace PolvorApp.Api.Tests.Orders;

/// <summary>
/// Design D4: an order write reads the edition <c>FOR SHARE</c> on its own transaction, so closing the
/// orders waits for it, a write that starts during a closing waits and then sees the orders closed, and
/// no FiringChief order write commits after the orders close (BR-10).
/// </summary>
public sealed class OrderWindowTests(PostgresFixture postgres, MailpitFixture mailpit)
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Closing_the_orders_waits_for_an_order_write_in_flight()
    {
        await using var host = await IdentityTestHost.StartAsync(postgres, mailpit);
        using var admin = await host.SignInAsync(await host.CreateUserAsync("admin.ventana@example.test", UserRole.Admin));
        var id = await OpenEditionAsync(host, admin);
        var version = (await admin.GetEditionAsync(id)).Version;

        await using var scope = host.Services.CreateAsyncScope();
        var orders = scope.ServiceProvider.GetRequiredService<ComparsaOrdersDbContext>();
        var directory = scope.ServiceProvider.GetRequiredService<IEditionDirectory>();
        Task<HttpResponseMessage>? closing = null;
        try
        {
            await using (var transaction = await orders.BeginWriteAsync(TestContext.Current.CancellationToken))
            {
                var during = await directory.ReadForOrderWriteAsync(id, transaction.GetDbTransaction(), TestContext.Current.CancellationToken);

                closing = admin.PostAsync($"/api/editions/{id}/orders", new { open = false, version });
                await WaitUntilBlockedOnAsync(host, "festival_editions");
                Assert.False(closing.IsCompleted);
                Assert.True(during!.OrdersOpen);
                await transaction.CommitAsync(TestContext.Current.CancellationToken);
            }

            using var closed = await closing;
            Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
        }
        finally
        {
            if (closing is { IsCompleted: false })
            {
                (await closing).Dispose();
            }
        }

        await using var after = await orders.BeginWriteAsync(TestContext.Current.CancellationToken);
        var snapshot = await directory.ReadForOrderWriteAsync(id, after.GetDbTransaction(), TestContext.Current.CancellationToken);
        Assert.False(snapshot!.OrdersOpen);
        Assert.Equal((2031, EditionStatus.InProgress, new DateOnly(2031, 4, 22), new DateOnly(2031, 4, 25)), (snapshot.Year, snapshot.Status, snapshot.FestivalStartsOn, snapshot.FestivalEndsOn));
    }

    [Fact]
    public async Task An_order_write_that_starts_during_a_closing_waits_and_sees_the_orders_closed()
    {
        await using var host = await IdentityTestHost.StartAsync(postgres, mailpit);
        using var admin = await host.SignInAsync(await host.CreateUserAsync("admin.ventana.inversa@example.test", UserRole.Admin));
        var id = await OpenEditionAsync(host, admin);

        await using var closingScope = host.Services.CreateAsyncScope();
        var editions = closingScope.ServiceProvider.GetRequiredService<FestivalEditionsDbContext>();
        await using var closingTransaction = await editions.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await editions.Database.ExecuteSqlAsync(
            $"UPDATE editions.festival_editions SET orders_open = false WHERE id = {id}", TestContext.Current.CancellationToken);

        await using var scope = host.Services.CreateAsyncScope();
        var orders = scope.ServiceProvider.GetRequiredService<ComparsaOrdersDbContext>();
        await using var transaction = await orders.BeginWriteAsync(TestContext.Current.CancellationToken);
        var reading = scope.ServiceProvider.GetRequiredService<IEditionDirectory>()
            .ReadForOrderWriteAsync(id, transaction.GetDbTransaction(), TestContext.Current.CancellationToken);
        await WaitUntilBlockedOnAsync(host, "festival_editions");
        Assert.False(reading.IsCompleted);
        await closingTransaction.CommitAsync(TestContext.Current.CancellationToken);

        Assert.False((await reading)!.OrdersOpen);
    }

    [Fact]
    public async Task A_read_that_waits_past_the_lock_timeout_fails_as_retryable()
    {
        await using var host = await IdentityTestHost.StartAsync(postgres, mailpit);
        using var admin = await host.SignInAsync(await host.CreateUserAsync("admin.ventana.espera@example.test", UserRole.Admin));
        var id = await OpenEditionAsync(host, admin);

        await using var holderScope = host.Services.CreateAsyncScope();
        var editions = holderScope.ServiceProvider.GetRequiredService<FestivalEditionsDbContext>();
        await using var held = await editions.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await editions.Database.SqlQuery<int>($"SELECT 1 AS \"Value\" FROM editions.festival_editions WHERE id = {id} FOR UPDATE")
            .ToListAsync(TestContext.Current.CancellationToken);

        await using var scope = host.Services.CreateAsyncScope();
        var orders = scope.ServiceProvider.GetRequiredService<ComparsaOrdersDbContext>();
        await using var transaction = await orders.BeginWriteAsync(TestContext.Current.CancellationToken);
        var error = await Assert.ThrowsAsync<PostgresException>(() => scope.ServiceProvider.GetRequiredService<IEditionDirectory>()
            .ReadForOrderWriteAsync(id, transaction.GetDbTransaction(), TestContext.Current.CancellationToken));

        Assert.Equal(PostgresErrorCodes.LockNotAvailable, error.SqlState);
    }

    [Fact]
    public async Task An_unknown_edition_reads_as_null()
    {
        await using var host = await IdentityTestHost.StartAsync(postgres, mailpit);
        await using var scope = host.Services.CreateAsyncScope();
        var orders = scope.ServiceProvider.GetRequiredService<ComparsaOrdersDbContext>();
        await using var transaction = await orders.BeginWriteAsync(TestContext.Current.CancellationToken);

        var snapshot = await scope.ServiceProvider.GetRequiredService<IEditionDirectory>()
            .ReadForOrderWriteAsync(Guid.CreateVersion7(), transaction.GetDbTransaction(), TestContext.Current.CancellationToken);

        Assert.Null(snapshot);
    }

    private static async Task<Guid> OpenEditionAsync(IdentityTestHost host, HttpClient admin)
    {
        var (id, _) = await admin.CreateCompleteEditionAsync(2031);
        await host.SetStateAsync(id, EditionStatus.InProgress, ordersOpen: true);
        return id;
    }

    /// <summary>Waits until some backend is waiting on a lock in a statement on <paramref name="table"/>.</summary>
    private static async Task WaitUntilBlockedOnAsync(IdentityTestHost host, string table)
    {
        var dataSource = host.Services.GetRequiredService<NpgsqlDataSource>();
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
