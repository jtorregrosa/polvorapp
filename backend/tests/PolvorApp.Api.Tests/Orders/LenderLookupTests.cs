using System.Net;
using System.Text.Json;
using PolvorApp.Api.Tests.Infrastructure;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Orders;

/// <summary>Spec "Lender lookup (UC-13, BR-12)" (design D9).</summary>
public sealed class LenderLookupTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private OrderTestHost _orders = null!;

    public async ValueTask InitializeAsync() => _orders = await OrderTestHost.StartAsync(postgres, mailpit);

    public async ValueTask DisposeAsync() => await _orders.DisposeAsync();

    [Fact]
    public async Task A_registered_lender_of_another_comparsa_is_found_without_guides()
    {
        var (lender, weapons) = await _orders.AddArquebusierAsync(_orders.Other.Id, "Prestamista Sintético", weapons: 2);

        using var response = await _orders.FiringChief.PostAsync("/api/comparsa-orders/lender-lookup", new { nationalId = lender.NationalId.ToLowerInvariant() });

        var body = await ReadAsync<JsonElement>(response);
        Assert.True(body.GetProperty("registered").GetBoolean());
        var found = body.GetProperty("lender");
        Assert.Equal(("Arcabucero", "Prestamista Sintético", _orders.Other.Name), (
            found.GetProperty("firstName").GetString(), found.GetProperty("lastName").GetString(), found.GetProperty("comparsaName").GetString()));
        Assert.Equal(weapons.Select(w => w.Id.ToString()), found.GetProperty("weapons").EnumerateArray().Select(w => w.GetProperty("id").GetString()));
        var text = body.GetRawText();
        Assert.DoesNotContain("GUIA", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("birth", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(lender.NationalId, text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_unregistered_owner_is_reported_as_such()
    {
        using var response = await _orders.FiringChief.PostAsync("/api/comparsa-orders/lender-lookup", new { nationalId = RegistryData.NextIdentity().NationalId });

        var body = await ReadAsync<JsonElement>(response);
        Assert.False(body.GetProperty("registered").GetBoolean());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("lender").ValueKind);
    }

    [Fact]
    public async Task An_invalid_national_id_is_refused_and_nothing_is_recorded()
    {
        using var response = await _orders.FiringChief.PostAsync("/api/comparsa-orders/lender-lookup", new { nationalId = "12345678A" });

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation");
        Assert.Empty(await _orders.Host.AuditEntriesAsync("LoanLenderLookedUp"));
    }

    [Fact]
    public async Task A_lookup_is_audited_without_the_identifier()
    {
        var (lender, _) = await _orders.AddArquebusierAsync(_orders.Other.Id, "Auditado Sintético");

        using var response = await _orders.FiringChief.PostAsync("/api/comparsa-orders/lender-lookup", new { nationalId = lender.NationalId });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var audit = Assert.Single(await _orders.Host.AuditEntriesAsync("LoanLenderLookedUp"));
        Assert.Equal(_orders.Registry.FiringChiefId, audit.ActorUserId);
        using var data = JsonDocument.Parse(audit.Data!);
        Assert.True(data.RootElement.GetProperty("found").GetBoolean());
        Assert.DoesNotContain(lender.NationalId, audit.Data, StringComparison.OrdinalIgnoreCase);
        Assert.Null(audit.EntityId);
    }

    [Fact]
    public async Task A_FiringChief_cannot_look_up_while_the_orders_are_closed()
    {
        await _orders.SetOrdersOpenAsync(false);

        using var chief = await _orders.FiringChief.PostAsync("/api/comparsa-orders/lender-lookup", new { nationalId = RegistryData.NextIdentity().NationalId });
        using var admin = await _orders.Admin.PostAsync("/api/comparsa-orders/lender-lookup", new { nationalId = RegistryData.NextIdentity().NationalId });

        await AssertProblemAsync(chief, HttpStatusCode.Conflict, "orders.closed");
        Assert.Equal(HttpStatusCode.OK, admin.StatusCode);
    }

    [Fact]
    public async Task A_lookup_that_finds_nobody_is_audited_too()
    {
        using var response = await _orders.FiringChief.PostAsync("/api/comparsa-orders/lender-lookup", new { nationalId = RegistryData.NextIdentity().NationalId });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var audit = Assert.Single(await _orders.Host.AuditEntriesAsync("LoanLenderLookedUp"));
        using var data = JsonDocument.Parse(audit.Data!);
        Assert.False(data.RootElement.GetProperty("found").GetBoolean());
    }

    [Fact]
    public async Task A_FiringChief_without_a_comparsa_cannot_look_up()
    {
        var chief = await _orders.Host.CreateUserAsync("jefe.sin.comparsa@example.test", PolvorApp.IdentityAccess.Contracts.UserRole.FiringChief);
        using var client = await _orders.Host.SignInAsync(chief);

        using var response = await client.PostAsync("/api/comparsa-orders/lender-lookup", new { nationalId = RegistryData.NextIdentity().NationalId });

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "orders.notFound");
        Assert.Empty(await _orders.Host.AuditEntriesAsync("LoanLenderLookedUp"));
    }

    [Fact]
    public async Task The_national_id_is_accepted_only_in_the_body()
    {
        using var response = await _orders.FiringChief.GetAsync(
            $"/api/comparsa-orders/lender-lookup?nationalId={RegistryData.NextIdentity().NationalId}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task Lookups_beyond_the_rate_limit_are_refused()
    {
        await using var limited = await OrderTestHost.StartAsync(
            postgres, mailpit, settings: new Dictionary<string, string?> { ["RateLimits:PersonalDataWrites:PermitLimit"] = "2" });

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
        {
            using var response = await limited.FiringChief.PostAsync("/api/comparsa-orders/lender-lookup", new { nationalId = RegistryData.NextIdentity().NationalId });
            statuses.Add(response.StatusCode);
        }

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], statuses);
    }
}
