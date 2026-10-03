using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ComparsaOrders.Entries;
using PolvorApp.FestivalEditions.Contracts;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;
using static PolvorApp.Api.Tests.Infrastructure.OrderData;

namespace PolvorApp.Api.Tests.Orders;

/// <summary>
/// Specs "Comparsa orders (UC-12)", "Preparing an order (UC-12)", "Pre-fill from the previous edition
/// (UC-12, BR-11)" and "Order visibility (BR-12)" (design D6, D8).
/// </summary>
public sealed class OrderPreparationTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private OrderTestHost _orders = null!;

    public async ValueTask InitializeAsync() => _orders = await OrderTestHost.StartAsync(postgres, mailpit);

    public async ValueTask DisposeAsync() => await _orders.DisposeAsync();

    [Fact]
    public async Task A_FiringChief_prepares_one_entry_per_arquebusier_sorted_in_spanish_order()
    {
        await _orders.AddArquebusierAsync(_orders.Own.Id, "Núñez Sintético");
        await _orders.AddArquebusierAsync(_orders.Own.Id, "Navarro Sintético", ArquebusierStatus.Reserve);
        await _orders.AddArquebusierAsync(_orders.Own.Id, "Ñúñez Sintético", weapons: 1);
        await _orders.AddArquebusierAsync(_orders.Other.Id, "Ajeno Sintético");

        var order = await _orders.PrepareAsync(_orders.FiringChief, _orders.Own.Id);

        Assert.Equal("DRAFT", order.GetProperty("status").GetString());
        Assert.Equal(["Navarro Sintético", "Núñez Sintético", "Ñúñez Sintético"], LastNames(order));
        var entries = order.GetProperty("entries").EnumerateArray().ToList();
        Assert.Equal(["RESERVE", "ACTIVE", "ACTIVE"], entries.Select(e => e.GetProperty("status").GetString()));
        Assert.Equal(["NONE", "NONE", "OWNED"], entries.Select(e => e.GetProperty("weaponSource").GetString()));
        Assert.Empty(order.GetProperty("notInOrder").EnumerateArray());
        Assert.True(order.GetProperty("canEdit").GetBoolean());
        var audit = Assert.Single(await _orders.Host.AuditEntriesAsync("ComparsaOrderPrepared"));
        Assert.Equal(_orders.Own.Id, audit.ComparsaId);
        using var data = JsonDocument.Parse(audit.Data!);
        Assert.Equal(3, data.RootElement.GetProperty("entryCount").GetInt32());
    }

    [Fact]
    public async Task The_order_offers_the_entry_choices_without_ownership_guides()
    {
        var (_, weapons) = await _orders.AddArquebusierAsync(_orders.Own.Id, "Dos Armas Sintético", weapons: 2);

        var order = await _orders.PrepareAsync(_orders.FiringChief, _orders.Own.Id);

        var offered = Assert.Single(order.GetProperty("offeredModels").EnumerateArray());
        Assert.Equal((_orders.Offered.Id.ToString(), _orders.Offered.Label), (offered.GetProperty("id").GetString(), offered.GetProperty("label").GetString()));
        var owned = Assert.Single(order.GetProperty("entries").EnumerateArray()).GetProperty("ownedWeapons").EnumerateArray().ToList();
        Assert.Equal(weapons.Select(w => w.Id.ToString()), owned.Select(w => w.GetProperty("id").GetString()));
        Assert.Equal(weapons.Select(w => w.WeaponNumber), owned.Select(w => w.GetProperty("weaponNumber").GetString()));
        Assert.All(owned, w => Assert.False(w.TryGetProperty("ownershipGuideNumber", out _)));
        Assert.DoesNotContain(weapons[0].OwnershipGuideNumber, order.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Entries_hold_their_history_copy()
    {
        var (arquebusier, weapons) = await _orders.AddArquebusierAsync(_orders.Own.Id, "Copia Sintética", weapons: 1);

        await _orders.PrepareAsync(_orders.FiringChief, _orders.Own.Id);

        var entry = await _orders.ReadOrdersAsync(db => db.Entries.AsNoTracking().SingleAsync(e => e.ArquebusierId == arquebusier.Id, TestContext.Current.CancellationToken));
        Assert.Equal(
            (arquebusier.FirstName, arquebusier.LastName, arquebusier.NationalId, (int?)arquebusier.FederationId),
            (entry.FirstName, entry.LastName, entry.NationalId, entry.FederationId));
        Assert.Equal((weapons[0].Id, weapons[0].WeaponNumber, weapons[0].OwnershipGuideNumber), (entry.OwnedWeaponId!.Value, entry.OwnedWeaponNumber, entry.OwnedWeaponGuideNumber));
    }

    [Fact]
    public async Task The_previous_editions_choices_are_copied()
    {
        var (arquebusier, _) = await _orders.AddArquebusierAsync(_orders.Own.Id, "Veterano Sintético");
        var previousOrder = NewOrder(_orders.Previous, _orders.Own.Id, PolvorApp.ComparsaOrders.Contracts.OrderStatus.Validated);
        var previous = NewEntry(previousOrder, arquebusier.Id);
        (previous.PowderKg, previous.CapsBoxes, previous.CapsType, previous.WeaponSource, previous.RentalWeaponModelId, previous.Flask) =
            (2, 3, CapsType.Normal, WeaponSource.Rental, _orders.Offered.Id, FlaskOption.Rental2Kg);
        await _orders.Services.SaveOrdersAsync(previousOrder, previous);

        var order = await _orders.PrepareAsync(_orders.FiringChief, _orders.Own.Id);

        var entry = Assert.Single(order.GetProperty("entries").EnumerateArray());
        Assert.Equal(
            (2, 3, "NORMAL", "RENTAL", _orders.Offered.Id.ToString(), "RENTAL_2KG"),
            (entry.GetProperty("powderKg").GetInt32(), entry.GetProperty("capsBoxes").GetInt32(), entry.GetProperty("capsType").GetString(),
             entry.GetProperty("weaponSource").GetString(), entry.GetProperty("rentalWeaponModel").GetProperty("id").GetString(), entry.GetProperty("flask").GetString()));
    }

    [Fact]
    public async Task An_arquebusier_with_an_entry_in_another_order_is_left_out()
    {
        var (moved, _) = await _orders.AddArquebusierAsync(_orders.Own.Id, "Trasladado Sintético");
        await _orders.AddArquebusierAsync(_orders.Own.Id, "Quieto Sintético");
        var otherOrder = NewOrder(_orders.Current, _orders.Other.Id);
        await _orders.Services.SaveOrdersAsync(otherOrder, NewEntry(otherOrder, moved.Id));

        var order = await _orders.PrepareAsync(_orders.FiringChief, _orders.Own.Id);

        Assert.Equal(["Quieto Sintético"], LastNames(order));
        Assert.Empty(order.GetProperty("notInOrder").EnumerateArray());
    }

    [Fact]
    public async Task Two_preparations_at_once_create_one_order()
    {
        await using var host = await OrderTestHost.StartAsync(postgres, mailpit, services => BarrierAuditTrail.Decorate(services, "ComparsaOrderPrepared", parties: 2));
        await host.AddArquebusierAsync(host.Own.Id, "Carrera Sintética");

        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ =>
            host.FiringChief.PostAsync("/api/comparsa-orders", new { editionId = host.Current.Id, comparsaId = host.Own.Id })));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        await AssertProblemAsync(Assert.Single(responses, r => r.StatusCode != HttpStatusCode.Created), HttpStatusCode.Conflict, "orders.alreadyPrepared");
        Assert.Single(await host.Host.AuditEntriesAsync("ComparsaOrderPrepared"));
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task A_second_preparation_is_refused()
    {
        await _orders.PrepareAsync(_orders.FiringChief, _orders.Own.Id);

        using var response = await PostPrepareAsync(_orders.Admin, _orders.Current.Id, _orders.Own.Id);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "orders.alreadyPrepared");
    }

    [Fact]
    public async Task Closed_orders_refuse_a_FiringChief_but_not_an_Admin()
    {
        await _orders.SetOrdersOpenAsync(false);

        using var chief = await PostPrepareAsync(_orders.FiringChief, _orders.Current.Id, _orders.Own.Id);
        using var admin = await PostPrepareAsync(_orders.Admin, _orders.Current.Id, _orders.Own.Id);

        await AssertProblemAsync(chief, HttpStatusCode.Conflict, "orders.closed");
        Assert.Equal(HttpStatusCode.Created, admin.StatusCode);
    }

    [Fact]
    public async Task An_Admin_prepares_in_a_closed_edition()
    {
        await _orders.SetCurrentStatusAsync(EditionStatus.Closed);

        using var response = await PostPrepareAsync(_orders.Admin, _orders.Current.Id, _orders.Own.Id);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task A_draft_edition_is_not_started_for_an_Admin_and_missing_for_a_FiringChief()
    {
        await _orders.SetCurrentStatusAsync(EditionStatus.Draft);

        using var admin = await PostPrepareAsync(_orders.Admin, _orders.Current.Id, _orders.Own.Id);
        using var chief = await PostPrepareAsync(_orders.FiringChief, _orders.Current.Id, _orders.Own.Id);

        await AssertProblemAsync(admin, HttpStatusCode.Conflict, "orders.editionNotStarted");
        await AssertProblemAsync(chief, HttpStatusCode.NotFound, "orders.notFound");
    }

    [Fact]
    public async Task An_inactive_comparsa_cannot_prepare()
    {
        using var response = await PostPrepareAsync(_orders.Admin, _orders.Current.Id, _orders.Inactive.Id);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "orders.comparsaInactive");
    }

    [Fact]
    public async Task A_comparsa_outside_the_scope_is_not_found_and_nothing_is_created()
    {
        using var response = await PostPrepareAsync(_orders.FiringChief, _orders.Current.Id, _orders.Other.Id);

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "orders.notFound");
        Assert.Equal(0, await _orders.ReadOrdersAsync(db => db.Orders.CountAsync(TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task An_order_outside_the_scope_reads_as_missing()
    {
        var order = await _orders.PrepareAsync(_orders.Admin, _orders.Other.Id);

        using var response = await _orders.FiringChief.GetAsync($"/api/comparsa-orders/{Id(order)}", TestContext.Current.CancellationToken);

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "orders.notFound");
    }

    [Fact]
    public async Task An_order_of_a_draft_edition_reads_as_missing_for_a_FiringChief_only()
    {
        var order = await _orders.PrepareAsync(_orders.Admin, _orders.Own.Id);
        await _orders.SetCurrentStatusAsync(EditionStatus.Draft);

        using var chief = await _orders.FiringChief.GetAsync($"/api/comparsa-orders/{Id(order)}", TestContext.Current.CancellationToken);
        using var admin = await _orders.Admin.GetAsync($"/api/comparsa-orders/{Id(order)}", TestContext.Current.CancellationToken);

        await AssertProblemAsync(chief, HttpStatusCode.NotFound, "orders.notFound");
        Assert.Equal(HttpStatusCode.OK, admin.StatusCode);
    }

    [Fact]
    public async Task A_FiringChief_reads_their_order_read_only_once_the_orders_close()
    {
        var order = await _orders.PrepareAsync(_orders.FiringChief, _orders.Own.Id);
        await _orders.SetOrdersOpenAsync(false);

        var read = await OrderTestHost.GetOrderAsync(_orders.FiringChief, Guid.Parse(Id(order)));
        var admin = await OrderTestHost.GetOrderAsync(_orders.Admin, Guid.Parse(Id(order)));

        Assert.Equal((false, "ordersClosed"), (read.GetProperty("canEdit").GetBoolean(), read.GetProperty("readOnlyReason").GetString()));
        Assert.True(admin.GetProperty("canEdit").GetBoolean());
    }

    [Fact]
    public async Task Missing_fields_are_named()
    {
        using var response = await _orders.FiringChief.PostAsync("/api/comparsa-orders", new { });

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation");
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("required", problem.RootElement.GetProperty("errors").GetProperty("editionId").GetString());
        Assert.Equal("required", problem.RootElement.GetProperty("errors").GetProperty("comparsaId").GetString());
    }

    private static Task<HttpResponseMessage> PostPrepareAsync(HttpClient client, Guid editionId, Guid comparsaId) =>
        client.PostAsync("/api/comparsa-orders", new { editionId, comparsaId });

    private static string Id(JsonElement order) => order.GetProperty("id").GetString()!;

    private static List<string?> LastNames(JsonElement order) =>
        [.. order.GetProperty("entries").EnumerateArray().Select(e => e.GetProperty("arquebusier").GetProperty("lastName").GetString())];
}
