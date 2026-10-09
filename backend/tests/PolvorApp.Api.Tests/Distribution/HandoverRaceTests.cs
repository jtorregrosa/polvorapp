using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.Distribution.Persistence;
using static PolvorApp.Api.Tests.Distribution.HandoverSyncEndpointTests;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Distribution;

/// <summary>
/// Spec "Handover sync and conflicts" (add-offline-distribution-capture D1, D2): devices syncing at the
/// same time record a holder and a flask number once, the other gets its reason, and a day held by
/// another write answers busy without storing anything.
/// </summary>
public sealed class HandoverRaceTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private OrderTestHost _orders = null!;
    private Arranged _day = null!;

    public async ValueTask InitializeAsync()
    {
        _orders = await OrderTestHost.StartAsync(postgres, mailpit);
        _day = await ArrangeAsync(_orders);
    }

    public async ValueTask DisposeAsync() => await _orders.DisposeAsync();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Two_devices_recording_the_same_holder_record_it_once()
    {
        var first = Item(_day.Holder.Id, flask: "P-117");
        var second = Item(_day.Holder.Id, flask: "P-200");

        var results = await Task.WhenAll(SyncAsync(_orders.Admin, _day.PowderDay, first), SyncAsync(_orders.Admin, _day.PowderDay, second));

        var outcomes = results.Select(r => Assert.Single(r)).ToList();
        var recorded = Assert.Single(outcomes, r => r.GetProperty("outcome").GetString() == "RECORDED");
        var refused = Assert.Single(outcomes, r => r.GetProperty("outcome").GetString() == "REFUSED");
        AssertRefused(refused, "distribution.alreadyHandedOver");
        Assert.Equal(recorded.GetProperty("id").GetGuid(), refused.GetProperty("existing").GetProperty("id").GetGuid());
        Assert.Equal(1, await CountAsync());
    }

    [Fact]
    public async Task The_same_handover_sent_twice_at_once_is_recorded_once()
    {
        var item = Item(_day.Holder.Id, flask: "P-117");

        var results = await Task.WhenAll(SyncAsync(_orders.Admin, _day.PowderDay, item), SyncAsync(_orders.Admin, _day.PowderDay, item));

        Assert.Equal(["ALREADY_RECORDED", "RECORDED"], results.Select(r => Assert.Single(r).GetProperty("outcome").GetString()).Order());
        Assert.Equal(1, await CountAsync());
    }

    [Fact]
    public async Task Two_devices_giving_the_same_flask_number_give_it_once()
    {
        var results = await Task.WhenAll(
            SyncAsync(_orders.Admin, _day.PowderDay, Item(_day.Holder.Id, flask: "P-117")),
            SyncAsync(_orders.Admin, _day.PowderDay, Item(_day.Small.Id, flask: "p-117")));

        var outcomes = results.Select(r => Assert.Single(r)).ToList();
        Assert.Single(outcomes, r => r.GetProperty("outcome").GetString() == "RECORDED");
        AssertRefused(Assert.Single(outcomes, r => r.GetProperty("outcome").GetString() == "REFUSED"), "distribution.flaskNumberTaken");
        Assert.Equal(1, await CountAsync());
    }

    [Fact]
    public async Task A_day_held_past_the_lock_timeout_answers_busy_and_stores_nothing()
    {
        await using var holder = _orders.Services.CreateAsyncScope();
        var db = holder.ServiceProvider.GetRequiredService<DistributionDbContext>();
        List<JsonElement> results;
        await using (var transaction = await db.Database.BeginTransactionAsync(Token))
        {
            Assert.True(await db.LockDayAsync(_day.PowderDay, Token));

            // The row stays locked for longer than the 5 s lock timeout of the sync.
            results = await SyncAsync(_orders.Admin, _day.PowderDay, Item(_day.Owned.Id));
            await transaction.RollbackAsync(Token);
        }

        AssertRefused(Assert.Single(results), "distribution.busy");
        Assert.Equal(0, await CountAsync());
    }

    [Fact]
    public async Task A_deleted_day_takes_no_more_handovers()
    {
        using var created = await PostAsync(_orders.Admin, _day.PowderDay, Item(_day.Owned.Id));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);

        await using var scope = _orders.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DistributionDbContext>();
        await db.Handovers.ExecuteDeleteAsync(Token);
        await db.Days.Where(d => d.Id == _day.PowderDay).ExecuteDeleteAsync(Token);

        using var response = await PostAsync(_orders.Admin, _day.PowderDay, Item(_day.Holder.Id, flask: "P-117"));

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "distribution.notFound");
    }

    private async Task<int> CountAsync()
    {
        await using var scope = _orders.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<DistributionDbContext>().Handovers.CountAsync(Token);
    }
}
