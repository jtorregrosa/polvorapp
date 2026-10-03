using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ArquebusierRegistry.Persistence;
using PolvorApp.ComparsaOrders.Contracts;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;
using static PolvorApp.Api.Tests.Infrastructure.OrderData;

namespace PolvorApp.Api.Tests.Orders;

/// <summary>
/// Specs "Adding arquebusiers to an order (UC-12)", "Edition entries (BR-05, BR-07)", "Who may edit
/// orders (BR-10)" and "Entry history (BR-14)" (design D8).
/// </summary>
public sealed class EntryEditingTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private OrderTestHost _orders = null!;

    public async ValueTask InitializeAsync() => _orders = await OrderTestHost.StartAsync(postgres, mailpit);

    public async ValueTask DisposeAsync() => await _orders.DisposeAsync();

    [Fact]
    public async Task An_arquebusier_registered_after_the_preparation_is_added_by_hand()
    {
        var order = await _orders.PrepareAsync(_orders.FiringChief, _orders.Own.Id);
        var (late, _) = await _orders.AddArquebusierAsync(_orders.Own.Id, "Tardío Sintético", weapons: 1);
        var before = await OrderTestHost.GetOrderAsync(_orders.FiringChief, OrderId(order));

        using var response = await _orders.FiringChief.PostAsync($"/api/comparsa-orders/{OrderId(order)}/entries", new { arquebusierId = late.Id });

        Assert.Equal([late.Id.ToString()], before.GetProperty("notInOrder").EnumerateArray().Select(a => a.GetProperty("arquebusierId").GetString()));
        var after = await ReadAsync<JsonElement>(response);
        var entry = Assert.Single(after.GetProperty("entries").EnumerateArray());
        Assert.Equal(("Tardío Sintético", "OWNED"), (entry.GetProperty("arquebusier").GetProperty("lastName").GetString(), entry.GetProperty("weaponSource").GetString()));
        Assert.Empty(after.GetProperty("notInOrder").EnumerateArray());
        Assert.Single(await _orders.Host.AuditEntriesAsync("EditionEntryAdded"));
    }

    [Fact]
    public async Task An_arquebusier_with_an_entry_elsewhere_in_the_edition_cannot_be_added()
    {
        var order = await _orders.PrepareAsync(_orders.FiringChief, _orders.Own.Id);
        var (transferred, _) = await _orders.AddArquebusierAsync(_orders.Own.Id, "Trasladado Sintético");
        var otherOrder = NewOrder(_orders.Current, _orders.Other.Id);
        await _orders.Services.SaveOrdersAsync(otherOrder, NewEntry(otherOrder, transferred.Id));

        using var response = await _orders.FiringChief.PostAsync($"/api/comparsa-orders/{OrderId(order)}/entries", new { arquebusierId = transferred.Id });

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "orders.alreadyInEdition");
    }

    [Fact]
    public async Task An_arquebusier_of_another_comparsa_cannot_be_added()
    {
        var order = await _orders.PrepareAsync(_orders.FiringChief, _orders.Own.Id);
        var (stranger, _) = await _orders.AddArquebusierAsync(_orders.Other.Id, "Ajeno Sintético");

        using var response = await _orders.FiringChief.PostAsync($"/api/comparsa-orders/{OrderId(order)}/entries", new { arquebusierId = stranger.Id });

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "orders.notFound");
    }

    [Fact]
    public async Task An_entry_takes_each_weapon_source_and_flask()
    {
        var (owner, weapons) = await _orders.AddArquebusierAsync(_orders.Own.Id, "Dueño Sintético", weapons: 2);
        var order = await _orders.PrepareAsync(_orders.FiringChief, _orders.Own.Id);
        var entry = Entry(order, owner.Id);

        var owned = await SaveAsync(order, entry, body => (body["weaponSource"], body["ownedWeaponId"], body["flask"]) = ("OWNED", weapons[1].Id, "OWNED"));
        var rental = await SaveAsync(order, owned, body => (body["weaponSource"], body["rentalWeaponModelId"], body["flask"], body["powderKg"]) = ("RENTAL", _orders.Offered.Id, "RENTAL_1KG", 1));
        var none = await SaveAsync(order, rental, body => (body["weaponSource"], body["flask"], body["capsBoxes"], body["capsType"]) = ("NONE", "RENTAL_2KG", 2, "SMALL"));

        Assert.Equal(("OWNED", weapons[1].Id.ToString(), "OWNED"), (owned.GetProperty("weaponSource").GetString(), owned.GetProperty("ownedWeapon").GetProperty("id").GetString(), owned.GetProperty("flask").GetString()));
        Assert.Equal(("RENTAL", _orders.Offered.Label, "RENTAL_1KG"), (rental.GetProperty("weaponSource").GetString(), rental.GetProperty("rentalWeaponModel").GetProperty("label").GetString(), rental.GetProperty("flask").GetString()));
        Assert.Equal(("NONE", 2, "SMALL"), (none.GetProperty("weaponSource").GetString(), none.GetProperty("capsBoxes").GetInt32(), none.GetProperty("capsType").GetString()));
        Assert.Equal(JsonValueKind.Null, none.GetProperty("ownedWeapon").ValueKind);
    }

    [Fact]
    public async Task Shooters_without_powder_and_carriers_without_a_weapon_are_saved()
    {
        var (shooter, weapons) = await _orders.AddArquebusierAsync(_orders.Own.Id, "Capitán Sintético", weapons: 1);
        var (carrier, _) = await _orders.AddArquebusierAsync(_orders.Own.Id, "Porteador Sintético");
        var order = await _orders.PrepareAsync(_orders.FiringChief, _orders.Own.Id);

        var savedShooter = await SaveAsync(order, Entry(order, shooter.Id), body => (body["powderKg"], body["weaponSource"], body["ownedWeaponId"]) = (0, "OWNED", weapons[0].Id));
        var savedCarrier = await SaveAsync(order, Entry(order, carrier.Id), body => (body["powderKg"], body["weaponSource"]) = (2, "NONE"));

        Assert.Equal((0, "OWNED"), (savedShooter.GetProperty("powderKg").GetInt32(), savedShooter.GetProperty("weaponSource").GetString()));
        Assert.Equal((2, "NONE"), (savedCarrier.GetProperty("powderKg").GetInt32(), savedCarrier.GetProperty("weaponSource").GetString()));
    }

    [Fact]
    public async Task Rule_breaking_values_are_refused_by_field()
    {
        var (arquebusier, _) = await _orders.AddArquebusierAsync(_orders.Own.Id, "Reglas Sintético");
        var order = await _orders.PrepareAsync(_orders.FiringChief, _orders.Own.Id);
        var entry = Entry(order, arquebusier.Id);

        using var reserve = await PutAsync(order, entry, body => (body["status"], body["powderKg"]) = ("RESERVE", 1));
        using var notOffered = await PutAsync(order, entry, body => (body["weaponSource"], body["rentalWeaponModelId"]) = ("RENTAL", _orders.NotOffered.Id));

        Assert.Equal("reserve", await ErrorAsync(reserve, "powderKg"));
        Assert.Equal("notOffered", await ErrorAsync(notOffered, "rentalWeaponModelId"));
    }

    [Fact]
    public async Task An_outdated_version_is_refused()
    {
        var (arquebusier, _) = await _orders.AddArquebusierAsync(_orders.Own.Id, "Versión Sintético");
        var order = await _orders.PrepareAsync(_orders.FiringChief, _orders.Own.Id);
        var entry = Entry(order, arquebusier.Id);
        await SaveAsync(order, entry, body => body["powderKg"] = 1);

        using var stale = await PutAsync(order, entry, body => body["powderKg"] = 2);

        await AssertProblemAsync(stale, HttpStatusCode.Conflict, "entries.modified");
    }

    [Fact]
    public async Task A_FiringChief_edit_sends_a_submitted_order_back_to_draft_and_keeps_a_returned_one()
    {
        var (arquebusier, _) = await _orders.AddArquebusierAsync(_orders.Own.Id, "Reenvío Sintético");
        var order = await _orders.PrepareAsync(_orders.FiringChief, _orders.Own.Id);
        await SetOrderStatusAsync(OrderId(order), OrderStatus.Submitted);

        var draft = await SaveOrderAsync(order, Entry(order, arquebusier.Id), body => body["powderKg"] = 1);
        await SetOrderStatusAsync(OrderId(order), OrderStatus.Returned, "Motivo sintético");
        var returned = await SaveOrderAsync(order, Entry(draft, arquebusier.Id), body => body["powderKg"] = 2);

        Assert.Equal("DRAFT", draft.GetProperty("status").GetString());
        Assert.Equal("RETURNED", returned.GetProperty("status").GetString());
        var audit = (await _orders.Host.AuditEntriesAsync("EditionEntryUpdated")).OrderBy(e => e.OccurredAt).First();
        using var data = JsonDocument.Parse(audit.Data!);
        Assert.Equal("DRAFT", data.RootElement.GetProperty("orderStatus").GetProperty("current").GetString());
    }

    [Fact]
    public async Task A_validated_order_is_read_only_for_a_FiringChief_but_not_for_an_Admin()
    {
        var (arquebusier, _) = await _orders.AddArquebusierAsync(_orders.Own.Id, "Validado Sintético");
        var order = await _orders.PrepareAsync(_orders.FiringChief, _orders.Own.Id);
        await SetOrderStatusAsync(OrderId(order), OrderStatus.Validated);
        var entry = Entry(order, arquebusier.Id);

        using var chief = await PutAsync(order, entry, body => body["powderKg"] = 1);
        using var admin = await PutAsync(order, entry, body => body["powderKg"] = 1, _orders.Admin);

        await AssertProblemAsync(chief, HttpStatusCode.Conflict, "orders.validated");
        Assert.Equal(HttpStatusCode.OK, admin.StatusCode);
        Assert.Equal("VALIDATED", (await ReadAsync<JsonElement>(admin)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Closed_orders_refuse_a_FiringChief_edit()
    {
        var (arquebusier, _) = await _orders.AddArquebusierAsync(_orders.Own.Id, "Cerrado Sintético");
        var order = await _orders.PrepareAsync(_orders.FiringChief, _orders.Own.Id);
        await _orders.SetOrdersOpenAsync(false);

        using var response = await PutAsync(order, Entry(order, arquebusier.Id), body => body["powderKg"] = 1);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "orders.closed");
    }

    [Fact]
    public async Task Each_save_refreshes_the_history_copy()
    {
        var (arquebusier, _) = await _orders.AddArquebusierAsync(_orders.Own.Id, "Antiguo Sintético");
        var order = await _orders.PrepareAsync(_orders.FiringChief, _orders.Own.Id);
        await using (var scope = _orders.Services.CreateAsyncScope())
        {
            var registry = scope.ServiceProvider.GetRequiredService<ArquebusierRegistryDbContext>();
            await registry.Arquebusiers.Where(a => a.Id == arquebusier.Id)
                .ExecuteUpdateAsync(a => a.SetProperty(x => x.LastName, "Corregido Sintético"), TestContext.Current.CancellationToken);
        }

        await SaveAsync(order, Entry(order, arquebusier.Id), body => body["powderKg"] = 1);

        var copy = await _orders.ReadOrdersAsync(db => db.Entries.AsNoTracking().SingleAsync(e => e.ArquebusierId == arquebusier.Id, TestContext.Current.CancellationToken));
        Assert.Equal("Corregido Sintético", copy.LastName);
    }

    [Fact]
    public async Task The_entry_of_an_arquebusier_no_longer_in_the_registry_can_still_be_set_to_reserve()
    {
        var order = await _orders.PrepareAsync(_orders.Admin, _orders.Own.Id);
        var history = NewEntry(await _orders.ReadOrdersAsync(db => db.Orders.AsNoTracking().SingleAsync(o => o.Id == OrderId(order), TestContext.Current.CancellationToken)), null);
        history.PowderKg = 2;
        await _orders.Services.SaveOrdersAsync(history);
        var reloaded = await OrderTestHost.GetOrderAsync(_orders.Admin, OrderId(order));
        var entry = reloaded.GetProperty("entries").EnumerateArray().Single(e => e.GetProperty("id").GetString() == history.Id.ToString());

        var saved = await SaveAsync(reloaded, entry, body => (body["status"], body["powderKg"]) = ("RESERVE", 0), _orders.Admin);

        Assert.Equal(("RESERVE", false, "Sintético Copia"), (saved.GetProperty("status").GetString(), saved.GetProperty("arquebusier").GetProperty("inRegistry").GetBoolean(), saved.GetProperty("arquebusier").GetProperty("lastName").GetString()));
    }

    [Fact]
    public async Task An_unchanged_save_records_nothing()
    {
        var (arquebusier, _) = await _orders.AddArquebusierAsync(_orders.Own.Id, "Igual Sintético");
        var order = await _orders.PrepareAsync(_orders.FiringChief, _orders.Own.Id);

        await SaveAsync(order, Entry(order, arquebusier.Id), _ => { });

        Assert.Empty(await _orders.Host.AuditEntriesAsync("EditionEntryUpdated"));
    }

    [Fact]
    public async Task An_edit_audits_the_changed_field_names_only()
    {
        var (arquebusier, _) = await _orders.AddArquebusierAsync(_orders.Own.Id, "Auditado Sintético");
        var order = await _orders.PrepareAsync(_orders.FiringChief, _orders.Own.Id);

        await SaveAsync(order, Entry(order, arquebusier.Id), body => (body["powderKg"], body["flask"]) = (2, "RENTAL_2KG"));

        var audit = Assert.Single(await _orders.Host.AuditEntriesAsync("EditionEntryUpdated"));
        using var data = JsonDocument.Parse(audit.Data!);
        Assert.Equal(["powderKg", "flask"], data.RootElement.GetProperty("fields").EnumerateArray().Select(f => f.GetString()));
        Assert.DoesNotContain("RENTAL_2KG", audit.Data, StringComparison.Ordinal);
        Assert.Equal(_orders.Own.Id, audit.ComparsaId);
    }

    [Fact]
    public async Task No_route_removes_an_entry()
    {
        var (arquebusier, _) = await _orders.AddArquebusierAsync(_orders.Own.Id, "Permanente Sintético");
        var order = await _orders.PrepareAsync(_orders.Admin, _orders.Own.Id);
        var entry = Entry(order, arquebusier.Id);

        using var response = await _orders.Admin.DeleteAsync($"/api/comparsa-orders/{OrderId(order)}/entries/{entry.GetProperty("id").GetString()}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Equal(1, await _orders.ReadOrdersAsync(db => db.Entries.CountAsync(TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task Entry_writes_on_an_order_outside_the_scope_are_not_found_even_with_closed_orders()
    {
        var (stranger, _) = await _orders.AddArquebusierAsync(_orders.Other.Id, "Fuera Sintético");
        var order = await _orders.PrepareAsync(_orders.Admin, _orders.Other.Id);
        await _orders.SetOrdersOpenAsync(false);

        using var add = await _orders.FiringChief.PostAsync($"/api/comparsa-orders/{OrderId(order)}/entries", new { arquebusierId = stranger.Id });
        using var edit = await PutAsync(order, Entry(order, stranger.Id), body => body["powderKg"] = 1);

        await AssertProblemAsync(add, HttpStatusCode.NotFound, "orders.notFound");
        await AssertProblemAsync(edit, HttpStatusCode.NotFound, "orders.notFound");
    }

    [Fact]
    public async Task An_entry_of_another_order_is_not_found()
    {
        var (own, _) = await _orders.AddArquebusierAsync(_orders.Own.Id, "Propio Sintético");
        var (stranger, _) = await _orders.AddArquebusierAsync(_orders.Other.Id, "Ajeno Sintético");
        var mine = await _orders.PrepareAsync(_orders.FiringChief, _orders.Own.Id);
        var theirs = await _orders.PrepareAsync(_orders.Admin, _orders.Other.Id);
        var foreign = Entry(theirs, stranger.Id);

        using var response = await _orders.FiringChief.PutAsJsonAsync(
            $"/api/comparsa-orders/{OrderId(mine)}/entries/{foreign.GetProperty("id").GetString()}",
            Body(foreign, body => body["powderKg"] = 1),
            TestContext.Current.CancellationToken);

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "entries.notFound");
        Assert.NotEqual(own.Id, stranger.Id);
    }

    [Fact]
    public async Task An_edit_without_a_version_is_a_bad_request()
    {
        var (arquebusier, _) = await _orders.AddArquebusierAsync(_orders.Own.Id, "Sin Versión Sintético");
        var order = await _orders.PrepareAsync(_orders.FiringChief, _orders.Own.Id);

        using var response = await PutAsync(order, Entry(order, arquebusier.Id), body => body.Remove("version"));

        Assert.Equal("required", await ErrorAsync(response, "version"));
    }

    [Fact]
    public async Task Adding_follows_the_closed_and_validated_rules()
    {
        var order = await _orders.PrepareAsync(_orders.FiringChief, _orders.Own.Id);
        var (late, _) = await _orders.AddArquebusierAsync(_orders.Own.Id, "Tarde Sintético");
        await SetOrderStatusAsync(OrderId(order), OrderStatus.Validated);

        using var validated = await _orders.FiringChief.PostAsync($"/api/comparsa-orders/{OrderId(order)}/entries", new { arquebusierId = late.Id });
        await _orders.SetOrdersOpenAsync(false);
        using var closed = await _orders.FiringChief.PostAsync($"/api/comparsa-orders/{OrderId(order)}/entries", new { arquebusierId = late.Id });

        await AssertProblemAsync(validated, HttpStatusCode.Conflict, "orders.validated");
        await AssertProblemAsync(closed, HttpStatusCode.Conflict, "orders.closed");
    }

    [Fact]
    public async Task Entry_writes_of_a_draft_edition_are_not_found_for_a_FiringChief_and_not_started_for_an_Admin()
    {
        var (arquebusier, _) = await _orders.AddArquebusierAsync(_orders.Own.Id, "Borrador Sintético");
        var order = await _orders.PrepareAsync(_orders.Admin, _orders.Own.Id);
        await _orders.SetCurrentStatusAsync(PolvorApp.FestivalEditions.Contracts.EditionStatus.Draft);
        var entry = Entry(order, arquebusier.Id);

        using var chief = await PutAsync(order, entry, body => body["powderKg"] = 1);
        using var admin = await PutAsync(order, entry, body => body["powderKg"] = 1, _orders.Admin);

        await AssertProblemAsync(chief, HttpStatusCode.NotFound, "orders.notFound");
        await AssertProblemAsync(admin, HttpStatusCode.Conflict, "orders.editionNotStarted");
    }

    [Fact]
    public async Task An_entry_whose_owned_weapon_was_removed_keeps_it_while_other_values_change()
    {
        var (owner, weapons) = await _orders.AddArquebusierAsync(_orders.Own.Id, "Vendió Sintético", weapons: 1);
        var order = await _orders.PrepareAsync(_orders.FiringChief, _orders.Own.Id);
        await using (var scope = _orders.Services.CreateAsyncScope())
        {
            var registry = scope.ServiceProvider.GetRequiredService<ArquebusierRegistryDbContext>();
            await registry.OwnedWeapons.Where(w => w.Id == weapons[0].Id).ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        }

        var reloaded = await OrderTestHost.GetOrderAsync(_orders.FiringChief, OrderId(order));
        var entry = Entry(reloaded, owner.Id);
        var saved = await SaveAsync(reloaded, entry, body => body["powderKg"] = 2);

        // The removed weapon shows its copy, ownership guide included (spec: Owned weapon kept in the history).
        Assert.Equal(
            (true, weapons[0].WeaponNumber, weapons[0].OwnershipGuideNumber),
            (entry.GetProperty("ownedWeapon").GetProperty("removed").GetBoolean(),
             entry.GetProperty("ownedWeapon").GetProperty("weaponNumber").GetString(),
             entry.GetProperty("ownedWeapon").GetProperty("ownershipGuideNumber").GetString()));
        Assert.Equal(("OWNED", 2, true, weapons[0].WeaponNumber), (
            saved.GetProperty("weaponSource").GetString(),
            saved.GetProperty("powderKg").GetInt32(),
            saved.GetProperty("ownedWeapon").GetProperty("removed").GetBoolean(),
            saved.GetProperty("ownedWeapon").GetProperty("weaponNumber").GetString()));
    }

    [Fact]
    public async Task The_owned_history_entry_of_an_arquebusier_no_longer_in_the_registry_keeps_its_weapon_while_edited()
    {
        var order = await _orders.PrepareAsync(_orders.Admin, _orders.Own.Id);
        var stored = await _orders.ReadOrdersAsync(db => db.Orders.AsNoTracking().SingleAsync(o => o.Id == OrderId(order), TestContext.Current.CancellationToken));
        var history = NewEntry(stored, null);
        (history.WeaponSource, history.OwnedWeaponModelId, history.OwnedWeaponNumber, history.OwnedWeaponGuideNumber) =
            (PolvorApp.ComparsaOrders.Contracts.WeaponSource.Owned, _orders.Offered.Id, "77-31", "GUIA-HISTORIA");
        await _orders.Services.SaveOrdersAsync(history);
        var reloaded = await OrderTestHost.GetOrderAsync(_orders.Admin, OrderId(order));
        var entry = reloaded.GetProperty("entries").EnumerateArray().Single(e => e.GetProperty("id").GetString() == history.Id.ToString());

        var saved = await SaveAsync(reloaded, entry, body => (body["capsBoxes"], body["capsType"]) = (1, "NORMAL"), _orders.Admin);

        Assert.Equal(("OWNED", "77-31", 1), (saved.GetProperty("weaponSource").GetString(), saved.GetProperty("ownedWeapon").GetProperty("weaponNumber").GetString(), saved.GetProperty("capsBoxes").GetInt32()));
    }

    private static Guid OrderId(JsonElement order) => Guid.Parse(order.GetProperty("id").GetString()!);

    private static JsonElement Entry(JsonElement order, Guid arquebusierId) =>
        order.GetProperty("entries").EnumerateArray().Single(e => e.GetProperty("arquebusier").GetProperty("id").GetString() == arquebusierId.ToString());

    /// <summary>The entry's current values as an edit body, changed by <paramref name="change"/>.</summary>
    private static Dictionary<string, object?> Body(JsonElement entry, Action<Dictionary<string, object?>> change)
    {
        var body = new Dictionary<string, object?>
        {
            ["version"] = entry.GetProperty("version").GetUInt32(),
            ["status"] = entry.GetProperty("status").GetString(),
            ["powderKg"] = entry.GetProperty("powderKg").GetInt32(),
            ["capsBoxes"] = entry.GetProperty("capsBoxes").GetInt32(),
            ["capsType"] = entry.GetProperty("capsType").ValueKind == JsonValueKind.Null ? null : entry.GetProperty("capsType").GetString(),
            ["weaponSource"] = entry.GetProperty("weaponSource").GetString(),
            ["ownedWeaponId"] = entry.GetProperty("ownedWeapon").ValueKind == JsonValueKind.Null ? null : entry.GetProperty("ownedWeapon").GetProperty("id").GetString(),
            ["rentalWeaponModelId"] = entry.GetProperty("rentalWeaponModel").ValueKind == JsonValueKind.Null ? null : entry.GetProperty("rentalWeaponModel").GetProperty("id").GetString(),
            ["flask"] = entry.GetProperty("flask").GetString(),
        };
        change(body);
        return body;
    }

    private Task<HttpResponseMessage> PutAsync(JsonElement order, JsonElement entry, Action<Dictionary<string, object?>> change, HttpClient? client = null) =>
        (client ?? _orders.FiringChief).PutAsJsonAsync(
            $"/api/comparsa-orders/{OrderId(order)}/entries/{entry.GetProperty("id").GetString()}", Body(entry, change), TestContext.Current.CancellationToken);

    /// <summary>Saves the entry and returns the order.</summary>
    private async Task<JsonElement> SaveOrderAsync(JsonElement order, JsonElement entry, Action<Dictionary<string, object?>> change, HttpClient? client = null)
    {
        using var response = await PutAsync(order, entry, change, client);
        return await ReadAsync<JsonElement>(response);
    }

    /// <summary>Saves the entry and returns it as the order now shows it.</summary>
    private async Task<JsonElement> SaveAsync(JsonElement order, JsonElement entry, Action<Dictionary<string, object?>> change, HttpClient? client = null)
    {
        var saved = await SaveOrderAsync(order, entry, change, client);
        var id = entry.GetProperty("id").GetString();
        return saved.GetProperty("entries").EnumerateArray().Single(e => e.GetProperty("id").GetString() == id);
    }

    private static async Task<string?> ErrorAsync(HttpResponseMessage response, string field)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return problem.RootElement.GetProperty("errors").GetProperty(field).GetString();
    }

    private async Task SetOrderStatusAsync(Guid orderId, OrderStatus status, string? reason = null)
    {
        await using var scope = _orders.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PolvorApp.ComparsaOrders.Persistence.ComparsaOrdersDbContext>();
        await db.Orders.Where(o => o.Id == orderId)
            .ExecuteUpdateAsync(o => o.SetProperty(x => x.Status, status).SetProperty(x => x.ReturnReason, reason), TestContext.Current.CancellationToken);
    }
}
