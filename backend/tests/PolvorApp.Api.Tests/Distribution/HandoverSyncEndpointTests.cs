using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.ComparsaOrders.Entries;
using PolvorApp.ComparsaOrders.Orders;
using PolvorApp.Distribution;
using PolvorApp.Distribution.Contracts;
using PolvorApp.Distribution.Days;
using PolvorApp.Distribution.Handovers;
using PolvorApp.Distribution.Persistence;
using PolvorApp.FestivalEditions.Contracts;
using PolvorApp.SharedKernel.Auditing;
using static PolvorApp.Api.Tests.Infrastructure.DistributionData;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Distribution;

/// <summary>
/// Specs "Powder handovers (UC-21)" and "Handover sync and conflicts" (add-offline-distribution-capture
/// D2, D3): each handover of a batch is checked and stored on its own, the device's id makes a resend
/// harmless, and every blocking rule answers its own reason.
/// </summary>
public sealed class HandoverSyncEndpointTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
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
    public async Task A_handover_collected_by_the_proxy_is_recorded_with_the_entry_kilograms()
    {
        var item = Item(_day.Holder.Id, collector: _day.Proxy.Id, flask: " P-117 ", traceability1: " A3 ", traceability2: "  ");

        var result = Assert.Single(await SyncAsync(_orders.Admin, _day.PowderDay, item));

        Assert.Equal("RECORDED", result.GetProperty("outcome").GetString());
        var stored = await StoredAsync(item.Id);
        Assert.Equal((HandoverCollector.Proxy, (Guid?)_day.Proxy.Id), (stored.CollectedBy, stored.CollectorEntryId));
        Assert.Equal(("P-117", "A3", (string?)null), (stored.RentalFlaskNumber, stored.Traceability1, stored.Traceability2));
        Assert.Equal((2, 4), (stored.PowderKg, stored.DistributionNumber));
        Assert.Equal(stored.Version, result.GetProperty("handover").GetProperty("version").GetUInt32());
    }

    [Fact]
    public async Task A_batch_sent_twice_records_each_handover_once()
    {
        var items = new[] { Item(_day.Holder.Id, flask: "P-117"), Item(_day.Owned.Id) };
        await SyncAsync(_orders.Admin, _day.PowderDay, items);

        var again = await SyncAsync(_orders.Admin, _day.PowderDay, items);

        Assert.All(again, r => Assert.Equal("ALREADY_RECORDED", r.GetProperty("outcome").GetString()));
        Assert.Equal(2, await CountAsync());
    }

    [Fact]
    public async Task The_same_id_with_other_data_is_refused()
    {
        var item = Item(_day.Holder.Id, flask: "P-117");
        await SyncAsync(_orders.Admin, _day.PowderDay, item);

        var result = Assert.Single(await SyncAsync(_orders.Admin, _day.PowderDay, item with { RentalFlaskNumber = "P-118" }));

        AssertRefused(result, "distribution.handoverChanged");
        Assert.Equal("P-117", (await StoredAsync(item.Id)).RentalFlaskNumber);
    }

    [Fact]
    public async Task A_time_in_another_offset_is_stored_in_utc_and_its_resend_matches()
    {
        var item = Item(_day.Owned.Id) with { CollectedAt = new DateTimeOffset(2031, 4, 18, 11, 30, 0, 123, TimeSpan.FromHours(2)).AddTicks(7) };
        await SyncAsync(_orders.Admin, _day.PowderDay, item);

        var resend = await SyncAsync(_orders.Admin, _day.PowderDay, item with { CollectedAt = item.CollectedAt.ToUniversalTime() });

        Assert.Equal("ALREADY_RECORDED", Assert.Single(resend).GetProperty("outcome").GetString());
        var stored = await StoredAsync(item.Id);
        Assert.Equal((TimeSpan.Zero, new DateTime(2031, 4, 18, 9, 30, 0, 123, DateTimeKind.Utc)), (stored.CollectedAt.Offset, stored.CollectedAt.UtcDateTime));
    }

    [Fact]
    public async Task An_id_of_another_day_is_refused_without_its_data()
    {
        var item = Item(_day.Owned.Id);
        await SyncAsync(_orders.Admin, _day.PowderDay, item);
        // The only other powder days belong to editions not in progress: one powder day per edition.
        var otherDay = new DistributionDay
        {
            Id = Guid.CreateVersion7(),
            EditionId = _orders.Previous.Id,
            Type = DistributionType.Powder,
            Date = new DateOnly(2030, 4, 18),
            Location = "Paraje Sintético",
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        await _orders.Services.SaveDistributionAsync(otherDay);

        var result = Assert.Single(await SyncAsync(_orders.Admin, otherDay.Id, item));

        AssertRefused(result, "distribution.editionNotInProgress");
        Assert.True(!result.TryGetProperty("handover", out var handover) || handover.ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public async Task The_same_id_twice_in_a_batch_is_recorded_once()
    {
        var item = Item(_day.Owned.Id);

        var results = await SyncAsync(_orders.Admin, _day.PowderDay, item, item);

        Assert.Equal(["RECORDED", "ALREADY_RECORDED"], results.Select(r => r.GetProperty("outcome").GetString()));
        Assert.Equal(1, await CountAsync());
    }

    [Fact]
    public async Task An_unreadable_handover_is_refused_on_its_own()
    {
        var unreadable = Item(_day.Owned.Id) with { CollectedBy = "NEIGHBOUR" };
        var readable = Item(_day.Holder.Id, flask: "P-117");

        var results = await SyncAsync(_orders.Admin, _day.PowderDay, unreadable, readable);

        AssertRefused(results[0], "distribution.invalid");
        Assert.Equal("RECORDED", results[1].GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task Each_refusal_has_its_reason_and_does_not_stop_the_others()
    {
        var first = Item(_day.Holder.Id, flask: "P-117");
        var items = new[]
        {
            first,
            Item(_day.Holder.Id, flask: "P-200"),
            Item(_day.Small.Id, flask: "p-117"),
            Item(_day.Small.Id),
            Item(_day.Owned.Id, flask: "P-300"),
            Item(_day.Pending.Id),
            Item(_day.Owned.Id, collector: _day.Small.Id),
            Item(_day.Small.Id, flask: new string('9', 21)),
            Item(_day.Small.Id, flask: "P-118", traceability1: new string('t', 51)),
        };

        var results = await SyncAsync(_orders.Admin, _day.PowderDay, items);

        Assert.Equal("RECORDED", results[0].GetProperty("outcome").GetString());
        AssertRefused(results[1], "distribution.alreadyHandedOver");
        Assert.Equal(first.Id, results[1].GetProperty("existing").GetProperty("id").GetGuid());
        AssertRefused(results[2], "distribution.flaskNumberTaken");
        AssertRefused(results[3], "distribution.flaskNumberRequired");
        AssertRefused(results[4], "distribution.flaskNumberNotRented");
        AssertRefused(results[5], "distribution.notInList");
        AssertRefused(results[6], "distribution.proxyNotValid");
        AssertRefused(results[7], "distribution.invalid");
        AssertRefused(results[8], "distribution.invalid");
        Assert.Equal(items.Select(i => i.Id), results.Select(r => r.GetProperty("id").GetGuid()));
        Assert.Equal(1, await CountAsync());
    }

    [Fact]
    public async Task A_closed_edition_refuses_every_handover()
    {
        await _orders.SetCurrentStatusAsync(EditionStatus.Closed);

        var results = await SyncAsync(_orders.Admin, _day.PowderDay, Item(_day.Holder.Id, flask: "P-117"), Item(_day.Owned.Id));

        Assert.All(results, r => AssertRefused(r, "distribution.editionNotInProgress"));
        Assert.Equal(0, await CountAsync());
    }

    [Fact]
    public async Task The_weapons_day_and_unknown_days_take_no_handovers()
    {
        using var planned = await DistributionRequests.PlanAsync(_orders.Admin, _orders.Current.Id, "WEAPONS", "2031-04-12", "Almacén Sintético");
        var weaponsDay = (await ReadAsync<JsonElement>(planned)).GetProperty("id").GetGuid();

        using var weapons = await PostAsync(_orders.Admin, weaponsDay, Item(_day.Owned.Id));
        using var unknown = await PostAsync(_orders.Admin, Guid.CreateVersion7(), Item(_day.Owned.Id));

        await AssertProblemAsync(weapons, HttpStatusCode.Conflict, "distribution.captureNotPowder");
        await AssertProblemAsync(unknown, HttpStatusCode.NotFound, "distribution.notFound");
    }

    [Fact]
    public async Task A_batch_over_one_hundred_or_without_ids_is_invalid()
    {
        using var tooMany = await PostAsync(_orders.Admin, _day.PowderDay, [.. Enumerable.Range(0, 101).Select(_ => Item(_day.Owned.Id))]);
        using var withoutId = await _orders.Admin.PostAsJsonAsync(
            $"/api/distribution/distributions/{_day.PowderDay}/handovers/sync",
            new { handovers = new[] { new { holderEntryId = _day.Owned.Id, distributionNumber = 1, collectedBy = "HOLDER", collectedAt = DateTimeOffset.UtcNow } } },
            Token);

        Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);
        Assert.Contains("handovers", await tooMany.Content.ReadAsStringAsync(Token), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, withoutId.StatusCode);
        Assert.Contains("handovers[0].id", await withoutId.Content.ReadAsStringAsync(Token), StringComparison.Ordinal);
        Assert.Equal(0, await CountAsync());
    }

    [Fact]
    public async Task A_batch_over_the_rate_limit_stores_none_of_it()
    {
        await using var limited = await OrderTestHost.StartAsync(postgres, mailpit, settings: new Dictionary<string, string?> { ["RateLimits:HandoverSync:PermitLimit"] = "1" });
        var day = await ArrangeAsync(limited);
        await SyncAsync(limited.Admin, day.PowderDay, Item(day.Owned.Id));

        using var second = await PostAsync(limited.Admin, day.PowderDay, Item(day.Holder.Id, flask: "P-117"));

        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        await using var scope = limited.Services.CreateAsyncScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<DistributionDbContext>().Handovers.CountAsync(Token));
    }

    [Fact]
    public async Task A_handover_whose_audit_entry_cannot_be_stored_is_not_recorded_and_stays_pending()
    {
        await using var unaudited = await OrderTestHost.StartAsync(postgres, mailpit, services =>
        {
            var original = services.Last(d => d.ServiceType == typeof(IAuditTrail));
            services.Remove(original);
            services.Add(new ServiceDescriptor(
                typeof(IAuditTrail),
                provider => new UnstorableAuditTrail((IAuditTrail)ActivatorUtilities.CreateInstance(provider, original.ImplementationType!)),
                original.Lifetime));
        });
        var day = await ArrangeAsync(unaudited);

        var results = await SyncAsync(unaudited.Admin, day.PowderDay, Item(day.Owned.Id), Item(day.Holder.Id, flask: "P-117"));

        Assert.All(results, result => AssertRefused(result, "distribution.busy"));
        await using var scope = unaudited.Services.CreateAsyncScope();
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<DistributionDbContext>().Handovers.CountAsync(Token));
    }

    [Fact]
    public async Task A_handover_keeps_its_number_when_a_later_list_renumbers_the_holder()
    {
        await SyncAsync(_orders.Admin, _day.PowderDay, Item(_day.Owned.Id) with { DistributionNumber = 3 });
        await _orders.AddLicensedEntryAsync(_day.Order, "Aaron Sintético", ValidThrough, powderKg: 1, flask: FlaskOption.Owned);

        using var response = await _orders.Admin.GetAsync($"/api/distribution/distributions/{_day.PowderDay}/capture", Token);
        var package = await ReadAsync<JsonElement>(response);

        var row = package.GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("entryId").GetGuid() == _day.Owned.Id);
        var handover = package.GetProperty("handovers").EnumerateArray().Single();
        Assert.Equal((4, 3), (row.GetProperty("number").GetInt32(), handover.GetProperty("distributionNumber").GetInt32()));
    }

    [Fact]
    public async Task A_FiringChief_cannot_sync()
    {
        using var response = await PostAsync(_orders.FiringChief, _day.PowderDay, Item(_day.Owned.Id));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, await CountAsync());
    }

    internal static HandoverItem Item(Guid holder, Guid? collector = null, string? flask = null, string? traceability1 = null, string? traceability2 = null) =>
        new(Guid.CreateVersion7(), holder, 4, collector is null ? "HOLDER" : "PROXY", collector, flask, traceability1, traceability2, new DateTimeOffset(2031, 4, 18, 9, 30, 0, TimeSpan.Zero));

    internal static Task<HttpResponseMessage> PostAsync(HttpClient client, Guid dayId, params HandoverItem[] items) =>
        client.PostAsJsonAsync($"/api/distribution/distributions/{dayId}/handovers/sync", new { handovers = items }, Token);

    internal static async Task<List<JsonElement>> SyncAsync(HttpClient client, Guid dayId, params HandoverItem[] items)
    {
        using var response = await PostAsync(client, dayId, items);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return [.. (await ReadAsync<JsonElement>(response)).GetProperty("results").EnumerateArray()];
    }

    internal static void AssertRefused(JsonElement result, string code) =>
        Assert.Equal(("REFUSED", code), (result.GetProperty("outcome").GetString(), result.GetProperty("code").GetString()));

    /// <summary>
    /// The powder day of Own's validated order: a 2 kg holder renting a 2 kg flask with a powder proxy, a
    /// 1 kg holder renting a 1 kg flask, a 1 kg holder with an owned flask, and Other's holder in a submitted
    /// order (not in the list).
    /// </summary>
    internal static async Task<Arranged> ArrangeAsync(OrderTestHost host)
    {
        var order = await host.AddOrderAsync(host.Current, host.Own.Id);
        var holder = await host.AddLicensedEntryAsync(order, "Abad Sintética", ValidThrough, powderKg: 2, flask: FlaskOption.Rental2Kg);
        var small = await host.AddLicensedEntryAsync(order, "Bernabeu Sintético", ValidThrough, powderKg: 1, flask: FlaskOption.Rental1Kg);
        var owned = await host.AddLicensedEntryAsync(order, "Climent Sintética", ValidThrough, powderKg: 1, flask: FlaskOption.Owned);
        var proxy = await host.AddLicensedEntryAsync(order, "Zamora Sintético", ValidThrough, status: ArquebusierStatus.Reserve);
        var submitted = await host.AddOrderAsync(host.Current, host.Other.Id, OrderStatus.Submitted);
        var pending = await host.AddLicensedEntryAsync(submitted, "Pendiente Sintética", ValidThrough, powderKg: 2);
        using var day = await DistributionRequests.PlanAsync(host.Admin, host.Current.Id, "POWDER", "2031-04-18", "Paraje Sintético");
        var dayId = (await ReadAsync<JsonElement>(day)).GetProperty("id").GetGuid();
        using var registered = await host.FiringChief.RegisterProxyAsync(host.Current.Id, holder.Id, proxy.Id);
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        return new Arranged(dayId, order, holder, small, owned, proxy, pending);
    }

    private async Task<Handover> StoredAsync(Guid id)
    {
        await using var scope = _orders.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<DistributionDbContext>().Handovers.AsNoTracking().SingleAsync(h => h.Id == id, Token);
    }

    private async Task<int> CountAsync()
    {
        await using var scope = _orders.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<DistributionDbContext>().Handovers.CountAsync(Token);
    }

    internal sealed record HandoverItem(
        Guid Id,
        Guid HolderEntryId,
        int DistributionNumber,
        string CollectedBy,
        Guid? CollectorEntryId,
        string? RentalFlaskNumber,
        string? Traceability1,
        string? Traceability2,
        DateTimeOffset CollectedAt);

    internal sealed record Arranged(
        Guid PowderDay, ComparsaOrder Order, EditionEntry Holder, EditionEntry Small, EditionEntry Owned, EditionEntry Proxy, EditionEntry Pending);

    /// <summary>Records a handover's entry as one the database refuses: an audit trail that cannot be stored.</summary>
    private sealed class UnstorableAuditTrail(IAuditTrail inner) : IAuditTrail
    {
        public void Record(DbContext context, AuditRecord record)
        {
            inner.Record(context, record);
            if (record.Action != DistributionAuditActions.HandoverRecorded)
            {
                return;
            }

            var entry = context.ChangeTracker.Entries<AuditEntry>().Last(e => e.State == EntityState.Added);
            entry.Property(e => e.Action).CurrentValue = new string('x', 200);
        }
    }
}
