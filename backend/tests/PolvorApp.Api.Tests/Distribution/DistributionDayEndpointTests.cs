using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.Distribution.Contracts;
using PolvorApp.Distribution.Persistence;
using PolvorApp.Distribution.Proxies;
using PolvorApp.FestivalEditions.Contracts;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;
using static PolvorApp.Api.Tests.Infrastructure.OrderData;

namespace PolvorApp.Api.Tests.Distribution;

/// <summary>
/// Spec "Distribution days (UC-18)" (design D4, D7, D8): Admins plan, edit and delete one powder and one
/// weapons day per edition in progress, with versions, validation and audit.
/// </summary>
public sealed class DistributionDayEndpointTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private OrderTestHost _orders = null!;

    public async ValueTask InitializeAsync() => _orders = await OrderTestHost.StartAsync(postgres, mailpit);

    public async ValueTask DisposeAsync() => await _orders.DisposeAsync();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_Admin_plans_the_powder_day_and_it_is_audited()
    {
        using var response = await DistributionRequests.PlanAsync(_orders.Admin, _orders.Current.Id, "POWDER", "2031-04-18", "  Paraje Sintético  ");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var day = await ReadAsync<JsonElement>(response);
        Assert.Equal(("POWDER", "2031-04-18", "Paraje Sintético"), (day.GetProperty("type").GetString(), day.GetProperty("date").GetString(), day.GetProperty("location").GetString()));
        Assert.Equal(0, day.GetProperty("slots").GetArrayLength());
        Assert.True(day.GetProperty("version").GetUInt32() > 0);
        var entry = Assert.Single(await _orders.Host.AuditEntriesAsync("DistributionPlanned"));
        Assert.Equal((_orders.Registry.AdminId, "Distribution", day.GetProperty("id").GetGuid().ToString()), (entry.ActorUserId, entry.EntityType, entry.EntityId));
    }

    [Fact]
    public async Task A_second_day_of_the_same_type_is_refused()
    {
        await PlanAsync("POWDER");
        await PlanAsync("WEAPONS");

        using var second = await DistributionRequests.PlanAsync(_orders.Admin, _orders.Current.Id, "POWDER", "2031-04-19", "Otro Paraje");

        await AssertProblemAsync(second, HttpStatusCode.Conflict, "distribution.alreadyPlanned");
        Assert.Equal(2, (await _orders.Host.AuditEntriesAsync("DistributionPlanned")).Count);
    }

    [Fact]
    public async Task Two_plans_of_the_same_type_at_once_leave_one_day()
    {
        var responses = await Task.WhenAll(
            DistributionRequests.PlanAsync(_orders.Admin, _orders.Current.Id, "WEAPONS", "2031-04-10", "Almacén Sintético"),
            DistributionRequests.PlanAsync(_orders.Admin, _orders.Current.Id, "WEAPONS", "2031-04-11", "Almacén Sintético"));

        Assert.Equal([HttpStatusCode.Created, HttpStatusCode.Conflict], responses.Select(r => r.StatusCode).Order());
        Assert.Single(await _orders.Host.AuditEntriesAsync("DistributionPlanned"));
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Theory]
    [InlineData("SALVAS", "2031-04-18", "Paraje", "type", "invalid")]
    [InlineData("POWDER", "", "Paraje", "date", "required")]
    [InlineData("POWDER", "18/04/2031", "Paraje", "date", "invalid")]
    [InlineData("POWDER", "2030-12-31", "Paraje", "date", "outOfEdition")]
    [InlineData("POWDER", "2031-04-26", "Paraje", "date", "outOfEdition")]
    [InlineData("POWDER", "2031-04-18", "   ", "location", "required")]
    [InlineData("POWDER", "2031-04-18", "Paraje\nSintético", "location", "invalid")]
    public async Task Invalid_fields_are_named(string type, string date, string location, string field, string reason)
    {
        using var response = await DistributionRequests.PlanAsync(_orders.Admin, _orders.Current.Id, type, date, location);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation");
        Assert.Equal(reason, (await ErrorsAsync(response))[field]);
        Assert.Empty(await _orders.Host.AuditEntriesAsync("DistributionPlanned"));
    }

    [Fact]
    public async Task A_location_over_200_characters_is_too_long()
    {
        using var response = await DistributionRequests.PlanAsync(_orders.Admin, _orders.Current.Id, "POWDER", "2031-04-18", new string('a', 201));

        Assert.Equal("tooLong", (await ErrorsAsync(response))["location"]);
    }

    [Fact]
    public async Task Only_the_edition_in_progress_is_planned()
    {
        var draft = NewEdition(2032, EditionStatus.Draft);
        await _orders.Services.SaveEditionsAsync(draft);

        using var closed = await DistributionRequests.PlanAsync(_orders.Admin, _orders.Previous.Id, "POWDER", "2030-04-18", "Paraje");
        using var inDraft = await DistributionRequests.PlanAsync(_orders.Admin, draft.Id, "POWDER", "2032-04-18", "Paraje");
        using var unknown = await DistributionRequests.PlanAsync(_orders.Admin, Guid.CreateVersion7(), "POWDER", "2031-04-18", "Paraje");

        await AssertProblemAsync(closed, HttpStatusCode.Conflict, "distribution.editionNotInProgress");
        await AssertProblemAsync(inDraft, HttpStatusCode.Conflict, "distribution.editionNotInProgress");
        await AssertProblemAsync(unknown, HttpStatusCode.NotFound, "distribution.notFound");
    }

    [Fact]
    public async Task A_FiringChief_cannot_plan_edit_or_delete()
    {
        var day = await PlanAsync("POWDER");

        using var plan = await DistributionRequests.PlanAsync(_orders.FiringChief, _orders.Current.Id, "WEAPONS", "2031-04-10", "Paraje");
        using var edit = await DistributionRequests.EditAsync(_orders.FiringChief, day.Id, "2031-04-19", "Paraje", day.Version);
        using var delete = await DistributionRequests.DeleteAsync(_orders.FiringChief, day.Id, day.Version);

        Assert.Equal([HttpStatusCode.Forbidden, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden], [plan.StatusCode, edit.StatusCode, delete.StatusCode]);
    }

    [Fact]
    public async Task An_edit_records_the_changed_fields_and_an_unchanged_save_records_nothing()
    {
        var day = await PlanAsync("POWDER");

        using var unchanged = await DistributionRequests.EditAsync(_orders.Admin, day.Id, "2031-04-18", "Paraje Sintético", day.Version);
        using var edit = await DistributionRequests.EditAsync(_orders.Admin, day.Id, "2031-04-19", "Paraje Sintético", day.Version);

        Assert.Equal(HttpStatusCode.OK, unchanged.StatusCode);
        var edited = await ReadAsync<JsonElement>(edit);
        Assert.Equal("2031-04-19", edited.GetProperty("date").GetString());
        var entry = Assert.Single(await _orders.Host.AuditEntriesAsync("DistributionEdited"));
        using var data = JsonDocument.Parse(entry.Data!);
        Assert.Equal("2031-04-18", data.RootElement.GetProperty("previous").GetProperty("date").GetString());
        Assert.Equal("2031-04-19", data.RootElement.GetProperty("current").GetProperty("date").GetString());
    }

    [Fact]
    public async Task An_outdated_version_is_refused()
    {
        var day = await PlanAsync("POWDER");
        using (var first = await DistributionRequests.EditAsync(_orders.Admin, day.Id, "2031-04-19", "Paraje Sintético", day.Version))
        {
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        }

        using var edit = await DistributionRequests.EditAsync(_orders.Admin, day.Id, "2031-04-20", "Paraje Sintético", day.Version);
        using var delete = await DistributionRequests.DeleteAsync(_orders.Admin, day.Id, day.Version);

        await AssertProblemAsync(edit, HttpStatusCode.Conflict, "distribution.modified");
        await AssertProblemAsync(delete, HttpStatusCode.Conflict, "distribution.modified");
    }

    [Fact]
    public async Task An_edit_on_a_closed_edition_is_refused()
    {
        var day = await PlanAsync("POWDER");
        await _orders.SetCurrentStatusAsync(EditionStatus.Closed);

        using var edit = await DistributionRequests.EditAsync(_orders.Admin, day.Id, "2031-04-19", "Paraje", day.Version);

        await AssertProblemAsync(edit, HttpStatusCode.Conflict, "distribution.editionNotInProgress");
    }

    [Fact]
    public async Task Deleting_a_day_removes_its_slots_keeps_the_proxies_and_is_audited()
    {
        var day = await PlanAsync("POWDER");
        using (var slots = await DistributionRequests.SaveSlotsAsync(_orders.Admin, day.Id, day.Version, (_orders.Own.Id, "09:00")))
        {
            day = await DayAsync(slots);
        }

        var order = NewOrder(_orders.Current, _orders.Own.Id);
        var holder = NewEntry(order, null);
        var proxy = NewEntry(order, null);
        await _orders.Services.SaveOrdersAsync(order, holder, proxy);
        await _orders.Services.SaveDistributionAsync(new PickupProxy
        {
            Id = Guid.CreateVersion7(),
            EditionId = _orders.Current.Id,
            ComparsaId = _orders.Own.Id,
            Type = DistributionType.Powder,
            HolderEntryId = holder.Id,
            ProxyEntryId = proxy.Id,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        using var delete = await DistributionRequests.DeleteAsync(_orders.Admin, day.Id, day.Version);

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        var plan = await DistributionRequests.PlanOfAsync(_orders.Admin, _orders.Current.Id);
        Assert.Empty(plan.GetProperty("days").EnumerateArray());
        Assert.Equal(1, await DistributionRequests.CountProxiesAsync(_orders.Services));
        var entry = Assert.Single(await _orders.Host.AuditEntriesAsync("DistributionDeleted"));
        using var data = JsonDocument.Parse(entry.Data!);
        Assert.Equal(1, data.RootElement.GetProperty("slots").GetInt32());
    }

    private async Task<DayRef> PlanAsync(string type)
    {
        using var response = await DistributionRequests.PlanAsync(_orders.Admin, _orders.Current.Id, type, type == "POWDER" ? "2031-04-18" : "2031-04-10", "Paraje Sintético");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await DayAsync(response);
    }

    private static async Task<DayRef> DayAsync(HttpResponseMessage response)
    {
        var day = await ReadAsync<JsonElement>(response);
        return new DayRef(day.GetProperty("id").GetGuid(), day.GetProperty("version").GetUInt32());
    }

    private sealed record DayRef(Guid Id, uint Version);
}
