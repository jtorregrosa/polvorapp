using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.FederationCatalog.Endpoints;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.IdentityAccess.Security;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Catalog;

/// <summary>
/// Spec "Assignments determine the comparsa scope" end to end: real assignment source, real
/// endpoints, the FiringChief stays signed in while an Admin changes their assignments.
/// </summary>
[Collection(PostgresGroup.Name)]
public sealed class AssignmentScopeTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private IdentityTestHost _host = null!;
    private HttpClient _admin = null!;
    private ComparsaResponse _norte = null!;
    private ComparsaResponse _sur = null!;

    public async ValueTask InitializeAsync()
    {
        _host = await IdentityTestHost.StartAsync(postgres, mailpit);
        _admin = await _host.SignInAsync(await _host.CreateUserAsync("admin.alcance@example.test", UserRole.Admin));
        _norte = await CreateComparsaAsync("Comparsa Sintética Norte");
        _sur = await CreateComparsaAsync("Comparsa Sintética Sur");
    }

    public async ValueTask DisposeAsync()
    {
        _admin.Dispose();
        await _host.DisposeAsync();
    }

    [Fact]
    public void The_host_uses_the_catalog_assignments_not_the_deny_all_default()
    {
        using var scope = _host.Services.CreateScope();

        var source = scope.ServiceProvider.GetRequiredService<IFiringChiefAssignmentSource>();

        Assert.IsNotType<NoFiringChiefAssignments>(source);
        Assert.Equal("PolvorApp.FederationCatalog", source.GetType().Assembly.GetName().Name);
    }

    [Fact]
    public async Task An_assignment_grants_the_scope_on_the_next_request_and_its_removal_withdraws_it()
    {
        var chief = await _host.CreateUserAsync("jefe.alcance@example.test");
        using var client = await _host.SignInAsync(chief);
        Assert.Empty(await ListAsync(client));

        (await _admin.PutAsync($"/api/comparsas/{_norte.Id}/firing-chiefs/{chief.Id}", null, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();

        Assert.Equal([_norte.Id], await ListAsync(client));
        using (var own = await client.GetAsync($"/api/comparsas/{_norte.Id}", TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        }

        (await _admin.DeleteAsync($"/api/comparsas/{_norte.Id}/firing-chiefs/{chief.Id}", TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();

        Assert.Empty(await ListAsync(client));
        using var gone = await client.GetAsync($"/api/comparsas/{_norte.Id}", TestContext.Current.CancellationToken);
        await AssertProblemAsync(gone, HttpStatusCode.NotFound, "comparsas.notFound");
    }

    [Fact]
    public async Task Deleting_a_comparsa_withdraws_it_from_its_firing_chiefs()
    {
        var chief = await _host.CreateUserAsync("jefe.borrada@example.test");
        (await _admin.PutAsync($"/api/comparsas/{_norte.Id}/firing-chiefs/{chief.Id}", null, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        (await _admin.PutAsync($"/api/comparsas/{_sur.Id}/firing-chiefs/{chief.Id}", null, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        using var client = await _host.SignInAsync(chief);
        Assert.Equal(2, (await ListAsync(client)).Count);

        (await _admin.DeleteAsync($"/api/comparsas/{_norte.Id}", TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();

        Assert.Equal([_sur.Id], await ListAsync(client));
        using var gone = await client.GetAsync($"/api/comparsas/{_norte.Id}", TestContext.Current.CancellationToken);
        await AssertProblemAsync(gone, HttpStatusCode.NotFound, "comparsas.notFound");
    }

    [Fact]
    public async Task A_firing_chief_promoted_to_admin_reaches_every_comparsa_and_keeps_the_assignment()
    {
        var chief = await _host.CreateUserAsync("jefe.ascendido@example.test");
        (await _admin.PutAsync($"/api/comparsas/{_norte.Id}/firing-chiefs/{chief.Id}", null, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        using var client = await _host.SignInAsync(chief);
        Assert.Equal([_norte.Id], await ListAsync(client));

        using var promoted = await _admin.PutAsJsonAsync(
            $"/api/users/{chief.Id}", new { name = "Jefe Ascendido", role = "ADMIN", locale = "es-ES" }, TestContext.Current.CancellationToken);
        promoted.EnsureSuccessStatusCode();

        Assert.Equal([_norte.Id, _sur.Id], await ListAsync(client));
        Assert.Equal([_norte.Id], await _host.AssignedComparsasAsync(chief.Id));
    }

    private static async Task<List<Guid>> ListAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/comparsas", TestContext.Current.CancellationToken);
        return (await ReadAsync<List<ComparsaResponse>>(response)).Select(c => c.Id).ToList();
    }

    private async Task<ComparsaResponse> CreateComparsaAsync(string name)
    {
        using var response = await _admin.PostAsync("/api/comparsas", new { name, side = "CHRISTIAN" });
        return await ReadAsync<ComparsaResponse>(response);
    }
}
