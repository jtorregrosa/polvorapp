using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.ComparsaOrders.Entries;
using PolvorApp.ComparsaOrders.Loans;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;
using static PolvorApp.Api.Tests.Infrastructure.OrderData;

namespace PolvorApp.Api.Tests.Orders;

/// <summary>
/// Cross-cutting guarantees of the orders API: every route needs a session and the review routes an
/// Admin; audit entries name the actor, the comparsa and the move, and refusals record nothing
/// (spec: Order changes are audited); writes are rate-limited; a deleted borrower's current entry
/// goes with its loan, external owner included (spec: Deleting an arquebusier).
/// </summary>
public sealed class OrderGuardAndAuditTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private OrderTestHost _orders = null!;

    public async ValueTask InitializeAsync() => _orders = await OrderTestHost.StartAsync(postgres, mailpit);

    public async ValueTask DisposeAsync() => await _orders.DisposeAsync();

    [Fact]
    public async Task Every_order_route_is_classified_and_needs_a_session()
    {
        string[] reviewed =
        [
            "GET api/comparsa-orders/overview",
            "GET api/comparsa-orders/{id:guid}",
            "POST api/comparsa-orders",
            "POST api/comparsa-orders/lender-lookup",
            "POST api/comparsa-orders/{id:guid}/entries",
            "POST api/comparsa-orders/{id:guid}/return",
            "POST api/comparsa-orders/{id:guid}/submit",
            "POST api/comparsa-orders/{id:guid}/validate",
            "PUT api/comparsa-orders/{id:guid}/entries/{entryId:guid}",
        ];
        var routes = _orders.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.Contains("comparsa-orders", StringComparison.Ordinal) == true)
            .SelectMany(e => (e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? ["GET"]).Select(m => $"{m} {e.RoutePattern.RawText!.Trim('/')}"))
            .Order(StringComparer.Ordinal)
            .ToList();
        Assert.Equal(reviewed.Order(StringComparer.Ordinal), routes);

        using var anonymous = _orders.Host.Factory.CreateClient();
        var id = Guid.CreateVersion7();
        foreach (var route in reviewed)
        {
            var parts = route.Split(' ');
            var path = "/" + parts[1].Replace("{id:guid}", id.ToString(), StringComparison.Ordinal).Replace("{entryId:guid}", id.ToString(), StringComparison.Ordinal);
            using var request = new HttpRequestMessage(new HttpMethod(parts[0]), path);
            if (parts[0] != "GET")
            {
                request.Content = JsonContent.Create(new { });
            }

            using var response = await anonymous.SendAsync(request, TestContext.Current.CancellationToken);

            // Writes without a session are stopped even earlier, by the antiforgery check (no token):
            // either way no handler runs.
            var refused = response.StatusCode == HttpStatusCode.Unauthorized
                || (parts[0] != "GET" && response.StatusCode == HttpStatusCode.BadRequest
                    && (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Contains("antiforgery.invalid", StringComparison.Ordinal));
            Assert.True(refused, $"{route} answered {response.StatusCode} without a session.");
        }
    }

    [Theory]
    [InlineData("validate")]
    [InlineData("return")]
    public async Task The_review_routes_refuse_a_FiringChief(string move)
    {
        var order = await _orders.PrepareAsync(_orders.FiringChief, _orders.Own.Id);

        using var response = await _orders.FiringChief.PostAsync(
            $"/api/comparsa-orders/{order.GetProperty("id").GetString()}/{move}",
            new { version = order.GetProperty("version").GetUInt32(), reason = "Motivo sintético" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(await _orders.Host.AuditEntriesAsync(move == "validate" ? "ComparsaOrderValidated" : "ComparsaOrderReturned"));
    }

    [Fact]
    public async Task An_Admin_submission_is_audited_with_the_actor_the_comparsa_and_the_move()
    {
        var order = await _orders.PrepareAsync(_orders.Admin, _orders.Own.Id);
        var orderId = order.GetProperty("id").GetString();

        using var response = await _orders.Admin.PostAsync($"/api/comparsa-orders/{orderId}/submit", new { version = order.GetProperty("version").GetUInt32() });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var audit = Assert.Single(await _orders.Host.AuditEntriesAsync("ComparsaOrderSubmitted"));
        Assert.Equal((_orders.Registry.AdminId, _orders.Own.Id, orderId), (audit.ActorUserId!.Value, audit.ComparsaId!.Value, audit.EntityId));
        using var data = JsonDocument.Parse(audit.Data!);
        Assert.Equal(
            ("DRAFT", "SUBMITTED", false, true),
            (data.RootElement.GetProperty("previous").GetString(), data.RootElement.GetProperty("current").GetString(),
             data.RootElement.GetProperty("attested").GetBoolean(), data.RootElement.GetProperty("byAdmin").GetBoolean()));
    }

    [Fact]
    public async Task Refused_moves_and_edits_record_no_audit_entry()
    {
        await _orders.AddArquebusierAsync(_orders.Own.Id, "Rechazado Sintético");
        var order = await _orders.PrepareAsync(_orders.FiringChief, _orders.Own.Id);
        var orderId = order.GetProperty("id").GetString();
        var version = order.GetProperty("version").GetUInt32();
        var entry = order.GetProperty("entries")[0];

        using var unattested = await _orders.FiringChief.PostAsync($"/api/comparsa-orders/{orderId}/submit", new { version });
        using var stale = await _orders.FiringChief.PostAsync($"/api/comparsa-orders/{orderId}/submit", new { version = version + 1, attestation = true });
        using var invalid = await _orders.FiringChief.PutAsJsonAsync(
            $"/api/comparsa-orders/{orderId}/entries/{entry.GetProperty("id").GetString()}",
            new { version = entry.GetProperty("version").GetUInt32(), status = "ACTIVE", powderKg = 5, capsBoxes = 0, weaponSource = "NONE", flask = "NONE" },
            TestContext.Current.CancellationToken);

        await AssertProblemAsync(unattested, HttpStatusCode.BadRequest, "validation");
        await AssertProblemAsync(stale, HttpStatusCode.Conflict, "orders.modified");
        await AssertProblemAsync(invalid, HttpStatusCode.BadRequest, "validation");
        Assert.Empty(await _orders.Host.AuditEntriesAsync("ComparsaOrderSubmitted"));
        Assert.Empty(await _orders.Host.AuditEntriesAsync("EditionEntryUpdated"));
    }

    [Fact]
    public async Task Order_writes_beyond_the_rate_limit_are_refused()
    {
        await using var limited = await OrderTestHost.StartAsync(
            postgres, mailpit, settings: new Dictionary<string, string?> { ["RateLimits:OrderWrites:PermitLimit"] = "2" });
        var unknown = Guid.CreateVersion7();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
        {
            using var response = await limited.FiringChief.PostAsync($"/api/comparsa-orders/{unknown}/submit", new { version = 1, attestation = true });
            statuses.Add(response.StatusCode);
        }

        Assert.Equal([HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.TooManyRequests], statuses);
    }

    [Fact]
    public async Task Deleting_a_borrower_with_open_orders_removes_their_entry_and_the_external_owner()
    {
        var (borrower, _) = await _orders.AddArquebusierAsync(_orders.Own.Id, "Prestatario Sintético");
        var order = NewOrder(_orders.Current, _orders.Own.Id);
        var entry = NewEntry(order, borrower.Id);
        entry.WeaponSource = WeaponSource.Loan;
        var externalNationalId = RegistryData.NextIdentity().NationalId;
        var loan = new WeaponLoan
        {
            Id = Guid.CreateVersion7(),
            EntryId = entry.Id,
            LenderKind = LenderKind.External,
            LenderFirstName = "Propietaria",
            LenderLastName = "Externa Borrable",
            LenderNationalId = externalNationalId,
            WeaponModelId = _orders.Offered.Id,
            WeaponNumber = "EXT-BORRABLE",
            OwnershipGuideNumber = "SINT-EXT-BORRABLE",
            CopiedAt = DateTimeOffset.UtcNow,
        };
        await _orders.Services.SaveOrdersAsync(order, entry, loan);

        using var response = await _orders.Admin.DeleteAsync($"/api/arquebusiers/{borrower.Id}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var (entries, loans) = await _orders.ReadOrdersAsync(async db => (
            await db.Entries.CountAsync(e => e.Id == entry.Id, TestContext.Current.CancellationToken),
            await db.Loans.CountAsync(l => l.LenderNationalId == externalNationalId, TestContext.Current.CancellationToken)));
        Assert.Equal((0, 0), (entries, loans));
        var audit = Assert.Single(await _orders.Host.AuditEntriesAsync("ArquebusierDeleted"));
        Assert.DoesNotContain(externalNationalId, audit.Data, StringComparison.Ordinal);
        Assert.DoesNotContain("Externa Borrable", audit.Data, StringComparison.Ordinal);
    }
}
