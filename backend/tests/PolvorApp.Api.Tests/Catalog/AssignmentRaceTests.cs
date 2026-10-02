using System.Net;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.FederationCatalog.Endpoints;
using PolvorApp.IdentityAccess.Contracts;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Catalog;

/// <summary>
/// Spec "FiringChief assignments": identical concurrent requests end with one assignment (or none)
/// and one audit entry. A barrier in the audit trail makes both requests pass their existence
/// check before either saves, so the race paths of design D4 run every time.
/// </summary>
public sealed class AssignmentRaceTests(PostgresFixture postgres, MailpitFixture mailpit)
{
    [Fact]
    public async Task Two_identical_assignments_racing_store_one_and_audit_one()
    {
        BarrierAuditTrail barrier = null!;
        await using var host = await IdentityTestHost.StartAsync(
            postgres, mailpit, configureServices: services => barrier = BarrierAuditTrail.Decorate(services, "FiringChiefAssigned", parties: 2));
        var (admin, comparsa, chief) = await ArrangeAsync(host);

        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ =>
            admin.PutAsync($"/api/comparsas/{comparsa.Id}/firing-chiefs/{chief.Id}", null, TestContext.Current.CancellationToken)));

        Assert.Equal(2, barrier.Arrived);
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.NoContent, r.StatusCode));
        Assert.Equal([comparsa.Id], await host.AssignedComparsasAsync(chief.Id));
        Assert.Single(await host.AuditEntriesAsync("FiringChiefAssigned"));
    }

    [Fact]
    public async Task Two_identical_removals_racing_remove_once_and_audit_once()
    {
        BarrierAuditTrail barrier = null!;
        await using var host = await IdentityTestHost.StartAsync(
            postgres, mailpit, configureServices: services => barrier = BarrierAuditTrail.Decorate(services, "FiringChiefUnassigned", parties: 2));
        var (admin, comparsa, chief) = await ArrangeAsync(host);
        await host.AssignAsync(comparsa.Id, chief.Id);

        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ =>
            admin.DeleteAsync($"/api/comparsas/{comparsa.Id}/firing-chiefs/{chief.Id}", TestContext.Current.CancellationToken)));

        Assert.Equal(2, barrier.Arrived);
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.NoContent, r.StatusCode));
        Assert.Empty(await host.AssignedComparsasAsync(chief.Id));
        Assert.Single(await host.AuditEntriesAsync("FiringChiefUnassigned"));
    }

    private static async Task<(HttpClient Admin, ComparsaResponse Comparsa, SyntheticUser Chief)> ArrangeAsync(IdentityTestHost host)
    {
        var admin = await host.SignInAsync(await host.CreateUserAsync("admin.carrera@example.test", UserRole.Admin));
        using var created = await admin.PostAsync("/api/comparsas", new { name = "Comparsa Sintética Carrera", side = "MOORISH" });
        return (admin, await ReadAsync<ComparsaResponse>(created), await host.CreateUserAsync("jefe.carrera@example.test"));
    }
}
