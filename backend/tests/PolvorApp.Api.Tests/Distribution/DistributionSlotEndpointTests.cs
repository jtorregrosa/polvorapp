using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.Distribution.Persistence;
using PolvorApp.FestivalEditions.Contracts;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;
using static PolvorApp.Api.Tests.Infrastructure.OrderData;
using static PolvorApp.Api.Tests.Infrastructure.RegistryData;

namespace PolvorApp.Api.Tests.Distribution;

/// <summary>
/// Spec "Distribution slots (UC-18)" and "Distribution visibility (BR-12)" (design D4, D7, D8): Admins save
/// a day's slots as one set; everyone reads the plan, FiringChiefs only their comparsas' slots.
/// </summary>
public sealed class DistributionSlotEndpointTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private OrderTestHost _orders = null!;

    public async ValueTask InitializeAsync() => _orders = await OrderTestHost.StartAsync(postgres, mailpit);

    public async ValueTask DisposeAsync() => await _orders.DisposeAsync();

    [Fact]
    public async Task An_Admin_saves_slots_and_sees_the_comparsas_without_one()
    {
        var day = await PlanAsync();

        using var response = await DistributionRequests.SaveSlotsAsync(_orders.Admin, day.Id, day.Version, (_orders.Other.Id, "09:30"), (_orders.Own.Id, "09:00"));

        var saved = await ReadAsync<JsonElement>(response);
        Assert.Equal([(_orders.Own.Id, "09:00"), (_orders.Other.Id, "09:30")], Slots(saved));
        Assert.True(saved.GetProperty("version").GetUInt32() != day.Version);
        var plan = await DistributionRequests.PlanOfAsync(_orders.Admin, _orders.Current.Id);
        var planned = Assert.Single(plan.GetProperty("days").EnumerateArray());
        Assert.DoesNotContain(planned.GetProperty("withoutSlot").EnumerateArray(), c => c.GetProperty("id").GetGuid() == _orders.Own.Id);
    }

    [Fact]
    public async Task Leaving_a_comparsa_out_removes_its_slot_and_the_change_is_audited()
    {
        var day = await PlanAsync();
        day = await SaveAsync(day, (_orders.Own.Id, "09:00"), (_orders.Other.Id, "09:30"));

        day = await SaveAsync(day, (_orders.Own.Id, "10:00"));

        var entries = await _orders.Host.AuditEntriesAsync("DistributionSlotsChanged");
        Assert.Equal(2, entries.Count);
        using var last = JsonDocument.Parse(Assert.Single(entries, e => e.Data!.Contains("10:00", StringComparison.Ordinal)).Data!);
        var changes = last.RootElement.GetProperty("changes").EnumerateArray()
            .ToDictionary(c => c.GetProperty("comparsaId").GetGuid(), c => (c.GetProperty("previous").GetString(), c.GetProperty("current").GetString()));
        Assert.Equal(("09:00", "10:00"), changes[_orders.Own.Id]);
        Assert.Equal(("09:30", (string?)null), changes[_orders.Other.Id]);
    }

    [Fact]
    public async Task An_unchanged_set_records_nothing()
    {
        var day = await PlanAsync();
        day = await SaveAsync(day, (_orders.Own.Id, "09:00"));

        using var again = await DistributionRequests.SaveSlotsAsync(_orders.Admin, day.Id, day.Version, (_orders.Own.Id, "09:00"));

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Single(await _orders.Host.AuditEntriesAsync("DistributionSlotsChanged"));
    }

    [Theory]
    [InlineData("24:30", "slots[0].startsAt", "invalid")]
    [InlineData("9:00", "slots[0].startsAt", "invalid")]
    [InlineData("", "slots[0].startsAt", "required")]
    public async Task An_invalid_time_is_named_by_index(string startsAt, string field, string reason)
    {
        var day = await PlanAsync();

        using var response = await DistributionRequests.SaveSlotsAsync(_orders.Admin, day.Id, day.Version, (_orders.Own.Id, startsAt));

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation");
        Assert.Equal(reason, (await ErrorsAsync(response))[field]);
    }

    [Fact]
    public async Task A_duplicate_or_unknown_comparsa_is_refused_and_nothing_changes()
    {
        var day = await PlanAsync();
        day = await SaveAsync(day, (_orders.Other.Id, "11:00"));

        using var duplicate = await DistributionRequests.SaveSlotsAsync(_orders.Admin, day.Id, day.Version, (_orders.Own.Id, "09:00"), (_orders.Own.Id, "10:00"));
        using var unknown = await DistributionRequests.SaveSlotsAsync(_orders.Admin, day.Id, day.Version, (_orders.Own.Id, "09:00"), (Guid.CreateVersion7(), "10:00"));

        Assert.Equal("duplicate", (await ErrorsAsync(duplicate))["slots[1].comparsaId"]);
        Assert.Equal("unknown", (await ErrorsAsync(unknown))["slots[1].comparsaId"]);
        var planned = Assert.Single((await DistributionRequests.PlanOfAsync(_orders.Admin, _orders.Current.Id)).GetProperty("days").EnumerateArray());
        Assert.Equal([(_orders.Other.Id, "11:00")], Slots(planned));
    }

    [Fact]
    public async Task Slots_need_the_current_version_an_edition_in_progress_and_an_Admin()
    {
        var day = await PlanAsync();
        var current = await SaveAsync(day, (_orders.Own.Id, "09:00"));

        using var outdated = await DistributionRequests.SaveSlotsAsync(_orders.Admin, day.Id, day.Version, (_orders.Own.Id, "10:00"));
        using var firingChief = await DistributionRequests.SaveSlotsAsync(_orders.FiringChief, day.Id, current.Version, (_orders.Own.Id, "10:00"));
        await _orders.SetCurrentStatusAsync(EditionStatus.Closed);
        using var closed = await DistributionRequests.SaveSlotsAsync(_orders.Admin, day.Id, current.Version, (_orders.Own.Id, "10:00"));

        await AssertProblemAsync(outdated, HttpStatusCode.Conflict, "distribution.modified");
        Assert.Equal(HttpStatusCode.Forbidden, firingChief.StatusCode);
        await AssertProblemAsync(closed, HttpStatusCode.Conflict, "distribution.editionNotInProgress");
    }

    [Fact]
    public async Task A_FiringChief_sees_the_days_and_only_their_slots()
    {
        var day = await PlanAsync();
        await SaveAsync(day, (_orders.Own.Id, "09:00"), (_orders.Other.Id, "09:30"));

        var plan = await DistributionRequests.PlanOfAsync(_orders.FiringChief, _orders.Current.Id);

        var planned = Assert.Single(plan.GetProperty("days").EnumerateArray());
        Assert.Equal(("2031-04-18", "Paraje Sintético"), (planned.GetProperty("date").GetString(), planned.GetProperty("location").GetString()));
        Assert.Equal([(_orders.Own.Id, "09:00")], Slots(planned));
        Assert.Equal(JsonValueKind.Null, planned.GetProperty("withoutSlot").ValueKind);
        Assert.Equal(JsonValueKind.Null, plan.GetProperty("notValidated").ValueKind);
        Assert.False(plan.GetProperty("canPlan").GetBoolean());
        Assert.True(plan.GetProperty("canManageProxies").GetBoolean());
    }

    [Fact]
    public async Task An_Admin_sees_which_orders_are_not_validated()
    {
        await _orders.Services.SaveOrdersAsync(NewOrder(_orders.Current, _orders.Own.Id, PolvorApp.ComparsaOrders.Contracts.OrderStatus.Validated), NewOrder(_orders.Current, _orders.Other.Id, PolvorApp.ComparsaOrders.Contracts.OrderStatus.Submitted));

        var plan = await DistributionRequests.PlanOfAsync(_orders.Admin, _orders.Current.Id);

        var notValidated = plan.GetProperty("notValidated").EnumerateArray().ToDictionary(c => c.GetProperty("comparsaId").GetGuid(), c => c.GetProperty("status").ValueKind == JsonValueKind.Null ? null : c.GetProperty("status").GetString());
        Assert.DoesNotContain(_orders.Own.Id, notValidated.Keys);
        Assert.Equal("SUBMITTED", notValidated[_orders.Other.Id]);
        Assert.True(plan.GetProperty("canPlan").GetBoolean());
    }

    [Fact]
    public async Task A_draft_or_unknown_edition_is_not_found_for_a_FiringChief()
    {
        var draft = NewEdition(2032, EditionStatus.Draft);
        await _orders.Services.SaveEditionsAsync(draft);

        using var asFiringChief = await _orders.FiringChief.GetAsync($"/api/distribution/editions/{draft.Id}", TestContext.Current.CancellationToken);
        using var unknown = await _orders.Admin.GetAsync($"/api/distribution/editions/{Guid.CreateVersion7()}", TestContext.Current.CancellationToken);
        using var asAdmin = await _orders.Admin.GetAsync($"/api/distribution/editions/{draft.Id}", TestContext.Current.CancellationToken);

        await AssertProblemAsync(asFiringChief, HttpStatusCode.NotFound, "distribution.notFound");
        await AssertProblemAsync(unknown, HttpStatusCode.NotFound, "distribution.notFound");
        Assert.Equal(HttpStatusCode.OK, asAdmin.StatusCode);
    }

    [Fact]
    public async Task A_closed_edition_is_read_only_for_FiringChiefs_and_Admins_still_manage_proxies()
    {
        await _orders.SetCurrentStatusAsync(EditionStatus.Closed);

        var asFiringChief = await DistributionRequests.PlanOfAsync(_orders.FiringChief, _orders.Current.Id);
        var asAdmin = await DistributionRequests.PlanOfAsync(_orders.Admin, _orders.Current.Id);

        Assert.Equal((false, false), (asFiringChief.GetProperty("canPlan").GetBoolean(), asFiringChief.GetProperty("canManageProxies").GetBoolean()));
        Assert.Equal((false, true), (asAdmin.GetProperty("canPlan").GetBoolean(), asAdmin.GetProperty("canManageProxies").GetBoolean()));
    }

    [Fact]
    public async Task A_missing_set_or_version_is_required_and_an_empty_set_clears_the_slots()
    {
        var day = await PlanAsync();
        day = await SaveAsync(day, (_orders.Own.Id, "09:00"));

        using var noSlots = await _orders.Admin.PutAsJsonAsync($"/api/distribution/distributions/{day.Id}/slots", new { version = day.Version }, TestContext.Current.CancellationToken);
        using var noVersion = await _orders.Admin.PutAsJsonAsync($"/api/distribution/distributions/{day.Id}/slots", new { slots = Array.Empty<object>() }, TestContext.Current.CancellationToken);
        Assert.Equal("required", (await ErrorsAsync(noSlots))["slots"]);
        Assert.Equal("required", (await ErrorsAsync(noVersion))["version"]);

        using var cleared = await DistributionRequests.SaveSlotsAsync(_orders.Admin, day.Id, day.Version);

        Assert.Empty(Slots(await ReadAsync<JsonElement>(cleared)));
    }

    [Fact]
    public async Task More_than_200_slots_are_refused()
    {
        var day = await PlanAsync();
        var slots = Enumerable.Range(0, 201).Select(_ => (Guid.CreateVersion7(), "09:00")).ToArray();

        using var response = await DistributionRequests.SaveSlotsAsync(_orders.Admin, day.Id, day.Version, slots);

        Assert.Equal("tooLong", (await ErrorsAsync(response))["slots"]);
    }

    [Fact]
    public async Task An_unknown_day_is_not_found()
    {
        var unknown = Guid.CreateVersion7();

        using var edit = await DistributionRequests.EditAsync(_orders.Admin, unknown, "2031-04-18", "Paraje", 1);
        using var delete = await DistributionRequests.DeleteAsync(_orders.Admin, unknown, 1);
        using var slots = await DistributionRequests.SaveSlotsAsync(_orders.Admin, unknown, 1, (_orders.Own.Id, "09:00"));
        using var noVersion = await _orders.Admin.DeleteAsync($"/api/distribution/distributions/{unknown}", TestContext.Current.CancellationToken);

        await AssertProblemAsync(edit, HttpStatusCode.NotFound, "distribution.notFound");
        await AssertProblemAsync(delete, HttpStatusCode.NotFound, "distribution.notFound");
        await AssertProblemAsync(slots, HttpStatusCode.NotFound, "distribution.notFound");
        Assert.Equal(HttpStatusCode.BadRequest, noVersion.StatusCode);
    }

    [Fact]
    public async Task A_day_locked_past_the_timeout_is_busy_and_nothing_changes()
    {
        var day = await PlanAsync();
        await using var holder = _orders.Services.CreateAsyncScope();
        var db = holder.ServiceProvider.GetRequiredService<DistributionDbContext>();
        await using (var transaction = await db.Database.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            Assert.True(await db.LockDayAsync(day.Id, TestContext.Current.CancellationToken));

            // The row stays locked for longer than the 5 s lock timeout of the save.
            using var response = await DistributionRequests.SaveSlotsAsync(_orders.Admin, day.Id, day.Version, (_orders.Own.Id, "09:00"));
            await transaction.RollbackAsync(TestContext.Current.CancellationToken);

            await AssertProblemAsync(response, HttpStatusCode.ServiceUnavailable, "distribution.busy");
        }

        var planned = Assert.Single((await DistributionRequests.PlanOfAsync(_orders.Admin, _orders.Current.Id)).GetProperty("days").EnumerateArray());
        Assert.Empty(Slots(planned));
        Assert.Empty(await _orders.Host.AuditEntriesAsync("DistributionSlotsChanged"));
    }

    private static List<(Guid, string)> Slots(JsonElement day) =>
        [.. day.GetProperty("slots").EnumerateArray().Select(s => (s.GetProperty("comparsaId").GetGuid(), s.GetProperty("startsAt").GetString()!))];

    private async Task<DayRef> PlanAsync()
    {
        using var response = await DistributionRequests.PlanAsync(_orders.Admin, _orders.Current.Id, "POWDER", "2031-04-18", "Paraje Sintético");
        var day = await ReadAsync<JsonElement>(response);
        return new DayRef(day.GetProperty("id").GetGuid(), day.GetProperty("version").GetUInt32());
    }

    private async Task<DayRef> SaveAsync(DayRef day, params (Guid ComparsaId, string StartsAt)[] slots)
    {
        using var response = await DistributionRequests.SaveSlotsAsync(_orders.Admin, day.Id, day.Version, slots);
        var saved = await ReadAsync<JsonElement>(response);
        return day with { Version = saved.GetProperty("version").GetUInt32() };
    }

    private sealed record DayRef(Guid Id, uint Version);
}
