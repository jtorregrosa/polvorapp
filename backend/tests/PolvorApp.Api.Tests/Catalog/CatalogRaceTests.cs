using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.FederationCatalog.Endpoints;
using PolvorApp.IdentityAccess.Contracts;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Catalog;

/// <summary>
/// Specs "Comparsas" and "Weapon models": the unique indexes decide when two requests pass the
/// uniqueness check together (design D3). A barrier in the audit trail holds both requests after
/// their check and before they save, so the race is lost by exactly one of them every time.
/// </summary>
public sealed class CatalogRaceTests(PostgresFixture postgres, MailpitFixture mailpit)
{
    [Fact]
    public async Task Two_comparsas_renamed_to_the_same_name_at_once_end_with_one_rename()
    {
        BarrierAuditTrail barrier = null!;
        await using var host = await IdentityTestHost.StartAsync(
            postgres, mailpit, configureServices: services => barrier = BarrierAuditTrail.Decorate(services, "ComparsaUpdated", parties: 2));
        var admin = await AdminAsync(host);
        var ids = new[] { await CreateComparsaAsync(admin, "Comparsa Sintética Uno"), await CreateComparsaAsync(admin, "Comparsa Sintética Dos") };

        var responses = await Task.WhenAll(ids.Select(id =>
            admin.PutAsJsonAsync($"/api/comparsas/{id}", new { name = "Comparsa Sintética Común", side = "CHRISTIAN" }, TestContext.Current.CancellationToken)));

        Assert.Equal(2, barrier.Arrived);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        await AssertProblemAsync(Assert.Single(responses, r => r.StatusCode != HttpStatusCode.OK), HttpStatusCode.Conflict, "comparsas.nameTaken");
        Assert.Single(await host.AuditEntriesAsync("ComparsaUpdated"));
    }

    [Theory]
    [InlineData("TRABUCO CARRERA", "TRABUCO CARRERA", "RIGHT", "LEFT", "weaponModels.labelTaken")]
    [InlineData("TRABUCO CARRERA", "TRABUCO CARRERA BIS", "RIGHT", "RIGHT", "weaponModels.combinationTaken")]
    public async Task Two_models_created_at_once_with_a_clash_store_one(
        string firstLabel, string secondLabel, string firstHand, string secondHand, string code)
    {
        BarrierAuditTrail barrier = null!;
        await using var host = await IdentityTestHost.StartAsync(
            postgres, mailpit, configureServices: services => barrier = BarrierAuditTrail.Decorate(services, "WeaponModelCreated", parties: 2));
        var admin = await AdminAsync(host);

        var responses = await Task.WhenAll(new[] { (firstLabel, firstHand), (secondLabel, secondHand) }.Select(model =>
            admin.PostAsync("/api/weapon-models", new
            {
                kind = "TRABUCO",
                side = "CHRISTIAN",
                handedness = model.Item2,
                size = "NORMAL",
                rentable = true,
                label = model.Item1,
            })));

        Assert.Equal(2, barrier.Arrived);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        await AssertProblemAsync(Assert.Single(responses, r => r.StatusCode != HttpStatusCode.Created), HttpStatusCode.Conflict, code);
        Assert.Single(await host.AuditEntriesAsync("WeaponModelCreated"));
    }

    [Fact]
    public async Task A_comparsa_in_use_by_any_module_is_not_deleted()
    {
        var unused = new FakeCatalogUsage();
        var inUse = new FakeCatalogUsage();
        await using var host = await IdentityTestHost.StartAsync(postgres, mailpit, configureServices: services =>
        {
            services.AddSingleton<ICatalogUsage>(unused);
            services.AddSingleton<ICatalogUsage>(inUse);
        });
        var admin = await AdminAsync(host);
        var comparsa = await CreateComparsaAsync(admin, "Comparsa Sintética Compartida");
        inUse.ComparsasInUse.Add(comparsa);

        using var response = await admin.DeleteAsync($"/api/comparsas/{comparsa}", TestContext.Current.CancellationToken);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "comparsas.inUse");
        Assert.Empty(await host.AuditEntriesAsync("ComparsaDeleted"));
    }

    private static async Task<HttpClient> AdminAsync(IdentityTestHost host) =>
        await host.SignInAsync(await host.CreateUserAsync("admin.carreras@example.test", UserRole.Admin));

    private static async Task<Guid> CreateComparsaAsync(HttpClient admin, string name)
    {
        using var created = await admin.PostAsync("/api/comparsas", new { name, side = "MOORISH" });
        return (await ReadAsync<ComparsaResponse>(created)).Id;
    }
}
