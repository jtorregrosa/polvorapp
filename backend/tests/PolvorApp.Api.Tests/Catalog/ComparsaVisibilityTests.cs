using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.FederationCatalog.Endpoints;
using PolvorApp.IdentityAccess.Contracts;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Catalog;

/// <summary>Spec "Comparsa visibility (BR-12)": Admins see every comparsa, FiringChiefs only theirs, others are 404.</summary>
[Collection(PostgresGroup.Name)]
public sealed class ComparsaVisibilityTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private readonly FakeAssignments _assignments = new();
    private IdentityTestHost _host = null!;
    private HttpClient _admin = null!;
    private ComparsaResponse _norte = null!;
    private ComparsaResponse _sur = null!;
    private ComparsaResponse _oeste = null!;

    public async ValueTask InitializeAsync()
    {
        _host = await IdentityTestHost.StartAsync(
            postgres, mailpit, configureServices: services => services.AddSingleton<IFiringChiefAssignmentSource>(_assignments));
        _admin = await _host.SignInAsync(await _host.CreateUserAsync("admin.visibilidad@example.test", UserRole.Admin));
        _sur = await CreateAsync("Comparsa Sintética Sur", "MOORISH");
        _norte = await CreateAsync("Comparsa Sintética Norte", "CHRISTIAN");
        _oeste = await CreateAsync("Comparsa Sintética Oeste", "MOORISH");
        using var deactivated = await _admin.PostAsync($"/api/comparsas/{_oeste.Id}/deactivate", new { });
        deactivated.EnsureSuccessStatusCode();
    }

    public async ValueTask DisposeAsync()
    {
        _admin.Dispose();
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task An_admin_sees_every_active_comparsa_sorted_by_name()
    {
        Assert.Equal(["Comparsa Sintética Norte", "Comparsa Sintética Sur"], (await ListAsync(_admin, string.Empty)).Select(c => c.Name));
    }

    [Fact]
    public async Task An_admin_can_include_inactive_comparsas_and_filter_by_side()
    {
        Assert.Equal(["Comparsa Sintética Norte", "Comparsa Sintética Oeste", "Comparsa Sintética Sur"], (await ListAsync(_admin, "?includeInactive=true")).Select(c => c.Name));
        Assert.Equal(["Comparsa Sintética Oeste", "Comparsa Sintética Sur"], (await ListAsync(_admin, "?side=MOORISH&includeInactive=true")).Select(c => c.Name));
        var oeste = Assert.Single(await ListAsync(_admin, "?side=MOORISH&includeInactive=true"), c => c.Id == _oeste.Id);
        Assert.Equal((Side.Moorish, false), (oeste.Side, oeste.Active));
    }

    [Fact]
    public async Task Names_are_sorted_in_spanish_order_whatever_the_database_collation()
    {
        await CreateAsync("Zeta Sintética", "MOORISH");
        await CreateAsync("Álamo Sintético", "MOORISH");
        await CreateAsync("comparsa sintética minúscula", "MOORISH");

        var names = (await ListAsync(_admin, string.Empty)).Select(c => c.Name).ToList();

        Assert.Equal(["Álamo Sintético", "comparsa sintética minúscula", "Comparsa Sintética Norte", "Comparsa Sintética Sur", "Zeta Sintética"], names);
    }

    [Fact]
    public async Task An_admin_gets_not_found_for_an_unknown_comparsa()
    {
        using var response = await _admin.GetAsync($"/api/comparsas/{Guid.CreateVersion7()}", TestContext.Current.CancellationToken);

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "comparsas.notFound");
    }

    [Fact]
    public async Task An_invalid_side_filter_is_named()
    {
        using var response = await _admin.GetAsync("/api/comparsas?side=NEUTRAL", TestContext.Current.CancellationToken);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation");
        Assert.Equal("invalid", (await ErrorsAsync(response))["side"]);
    }

    [Fact]
    public async Task A_firing_chief_sees_only_the_assigned_comparsas_including_inactive_ones()
    {
        var chief = await _host.CreateUserAsync("jefe.visibilidad@example.test");
        _assignments.Assign(chief.Id, _sur.Id, _oeste.Id);
        using var client = await _host.SignInAsync(chief);

        Assert.Equal([_sur.Id], (await ListAsync(client, string.Empty)).Select(c => c.Id));
        Assert.Equal([_oeste.Id, _sur.Id], (await ListAsync(client, "?includeInactive=true")).Select(c => c.Id));
        using var own = await client.GetAsync($"/api/comparsas/{_oeste.Id}", TestContext.Current.CancellationToken);
        Assert.Equal("Comparsa Sintética Oeste", (await ReadAsync<ComparsaResponse>(own)).Name);
    }

    [Fact]
    public async Task A_firing_chief_gets_not_found_for_a_comparsa_outside_their_scope()
    {
        var chief = await _host.CreateUserAsync("jefe.fuera@example.test");
        _assignments.Assign(chief.Id, _sur.Id);
        using var client = await _host.SignInAsync(chief);

        using var other = await client.GetAsync($"/api/comparsas/{_norte.Id}", TestContext.Current.CancellationToken);
        using var unknown = await client.GetAsync($"/api/comparsas/{Guid.CreateVersion7()}", TestContext.Current.CancellationToken);

        await AssertProblemAsync(other, HttpStatusCode.NotFound, "comparsas.notFound");
        await AssertProblemAsync(unknown, HttpStatusCode.NotFound, "comparsas.notFound");
    }

    [Fact]
    public async Task A_firing_chief_without_assignments_sees_no_comparsa()
    {
        using var client = await _host.SignInAsync(await _host.CreateUserAsync("jefe.sin.comparsa@example.test"));

        Assert.Empty(await ListAsync(client, "?includeInactive=true"));
    }

    [Fact]
    public async Task Comparsas_are_not_readable_without_signing_in()
    {
        using var client = await _host.NewClientAsync();

        using var response = await client.GetAsync("/api/comparsas", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<List<ComparsaResponse>> ListAsync(HttpClient client, string query)
    {
        using var response = await client.GetAsync($"/api/comparsas{query}", TestContext.Current.CancellationToken);
        return await ReadAsync<List<ComparsaResponse>>(response);
    }

    private async Task<ComparsaResponse> CreateAsync(string name, string side)
    {
        using var response = await _admin.PostAsync("/api/comparsas", new { name, side });
        return await ReadAsync<ComparsaResponse>(response);
    }

    private sealed class FakeAssignments : IFiringChiefAssignmentSource
    {
        private readonly ConcurrentDictionary<Guid, Guid[]> _byUser = new();

        public void Assign(Guid userId, params Guid[] comparsaIds) => _byUser[userId] = comparsaIds;

        public Task<IReadOnlySet<Guid>> GetComparsaIdsAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<Guid>>(_byUser.TryGetValue(userId, out var ids) ? ids.ToHashSet() : []);
    }
}
