using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.FederationCatalog.Endpoints;
using PolvorApp.IdentityAccess.Contracts;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Catalog;

/// <summary>Spec "FiringChief assignments" and the assignment part of "Catalogue changes are audited".</summary>
public sealed class AssignmentTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private IdentityTestHost _host = null!;
    private SyntheticUser _adminUser = null!;
    private HttpClient _admin = null!;
    private ComparsaResponse _norte = null!;

    public async ValueTask InitializeAsync()
    {
        _host = await IdentityTestHost.StartAsync(postgres, mailpit);
        _adminUser = await _host.CreateUserAsync("admin.asignaciones@example.test", UserRole.Admin);
        _admin = await _host.SignInAsync(_adminUser);
        _norte = await CreateComparsaAsync("Comparsa Sintética Norte");
    }

    public async ValueTask DisposeAsync()
    {
        _admin.Dispose();
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task An_admin_assigns_an_active_firing_chief_and_it_is_audited_with_the_comparsa()
    {
        var chief = await _host.CreateUserAsync("jefe.asignado@example.test");

        using var response = await AssignAsync(_norte.Id, chief.Id);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal([_norte.Id], await _host.AssignedComparsasAsync(chief.Id));
        var entry = Assert.Single(await _host.AuditEntriesAsync("FiringChiefAssigned"));
        Assert.Equal((_adminUser.Id, _norte.Id, "FiringChiefAssignment", chief.Id.ToString()), (entry.ActorUserId, entry.ComparsaId, entry.EntityType, entry.EntityId));
        using var data = JsonDocument.Parse(entry.Data!);
        Assert.Equal(chief.Id, data.RootElement.GetProperty("userId").GetGuid());
    }

    [Fact]
    public async Task An_invited_firing_chief_can_be_assigned()
    {
        var invited = await _host.CreateUserAsync("jefe.invitado@example.test", withPassword: false, enrolled: false);

        using var response = await AssignAsync(_norte.Id, invited.Id);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal([_norte.Id], await _host.AssignedComparsasAsync(invited.Id));
    }

    [Fact]
    public async Task Assigning_an_admin_is_blocking()
    {
        var admin = await _host.CreateUserAsync("otra.admin@example.test", UserRole.Admin);

        using var response = await AssignAsync(_norte.Id, admin.Id);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "assignments.notFiringChief");
        Assert.Empty(await _host.AssignedComparsasAsync(admin.Id));
    }

    [Fact]
    public async Task Assigning_a_deactivated_user_is_blocking()
    {
        var deactivated = await _host.CreateUserAsync("jefe.baja@example.test", active: false);

        using var response = await AssignAsync(_norte.Id, deactivated.Id);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "assignments.userDeactivated");
        Assert.Empty(await _host.AssignedComparsasAsync(deactivated.Id));
    }

    [Fact]
    public async Task Deactivating_a_firing_chief_keeps_their_assignments_for_a_later_reactivation()
    {
        var chief = await _host.CreateUserAsync("jefe.temporada@example.test");
        (await AssignAsync(_norte.Id, chief.Id)).Dispose();

        using var deactivated = await _admin.PostAsync($"/api/users/{chief.Id}/deactivate", new { });

        deactivated.EnsureSuccessStatusCode();
        Assert.Equal([_norte.Id], await _host.AssignedComparsasAsync(chief.Id));
        Assert.Empty(await _host.AuditEntriesAsync("FiringChiefUnassigned"));
    }

    [Fact]
    public async Task Assigning_to_an_inactive_comparsa_is_blocking()
    {
        var chief = await _host.CreateUserAsync("jefe.inactiva@example.test");
        var dormida = await CreateComparsaAsync("Comparsa Sintética Dormida");
        using var deactivated = await _admin.PostAsync($"/api/comparsas/{dormida.Id}/deactivate", new { });
        deactivated.EnsureSuccessStatusCode();

        using var response = await AssignAsync(dormida.Id, chief.Id);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "assignments.comparsaInactive");
        Assert.Empty(await _host.AssignedComparsasAsync(chief.Id));
    }

    [Fact]
    public async Task An_unknown_user_or_comparsa_is_not_found()
    {
        var chief = await _host.CreateUserAsync("jefe.desconocido@example.test");

        using var unknownUser = await AssignAsync(_norte.Id, Guid.CreateVersion7());
        using var unknownComparsa = await AssignAsync(Guid.CreateVersion7(), chief.Id);
        using var removeUnknownUser = await UnassignAsync(_norte.Id, Guid.CreateVersion7());
        using var removeUnknownComparsa = await UnassignAsync(Guid.CreateVersion7(), chief.Id);

        await AssertProblemAsync(unknownUser, HttpStatusCode.NotFound, "assignments.userNotFound");
        await AssertProblemAsync(unknownComparsa, HttpStatusCode.NotFound, "comparsas.notFound");
        await AssertProblemAsync(removeUnknownUser, HttpStatusCode.NotFound, "assignments.userNotFound");
        await AssertProblemAsync(removeUnknownComparsa, HttpStatusCode.NotFound, "comparsas.notFound");
    }

    [Fact]
    public async Task Repeating_an_assignment_or_removing_a_missing_one_changes_nothing()
    {
        var chief = await _host.CreateUserAsync("jefe.repetido@example.test");

        using var first = await AssignAsync(_norte.Id, chief.Id);
        using var again = await AssignAsync(_norte.Id, chief.Id);
        using var removed = await UnassignAsync(_norte.Id, chief.Id);
        using var removedAgain = await UnassignAsync(_norte.Id, chief.Id);

        Assert.All([first, again, removed, removedAgain], r => Assert.Equal(HttpStatusCode.NoContent, r.StatusCode));
        Assert.Single(await _host.AuditEntriesAsync("FiringChiefAssigned"));
        Assert.Single(await _host.AuditEntriesAsync("FiringChiefUnassigned"));
    }

    [Fact]
    public async Task Repeating_an_assignment_that_is_no_longer_eligible_still_succeeds()
    {
        var chief = await _host.CreateUserAsync("jefe.antiguo@example.test");
        var dormida = await CreateComparsaAsync("Comparsa Sintética Adormecida");
        using var assigned = await AssignAsync(dormida.Id, chief.Id);
        using var deactivated = await _admin.PostAsync($"/api/comparsas/{dormida.Id}/deactivate", new { });
        deactivated.EnsureSuccessStatusCode();

        using var again = await AssignAsync(dormida.Id, chief.Id);

        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
        Assert.Single(await _host.AuditEntriesAsync("FiringChiefAssigned"));
    }

    [Fact]
    public async Task The_kept_assignment_of_a_user_promoted_to_admin_can_be_removed()
    {
        var chief = await _host.CreateUserAsync("jefe.promovido@example.test");
        using var assigned = await AssignAsync(_norte.Id, chief.Id);
        using var promoted = await _admin.PutAsJsonAsync(
            $"/api/users/{chief.Id}", new { name = "Jefe Promovido", role = "ADMIN", locale = "es-ES" }, TestContext.Current.CancellationToken);
        promoted.EnsureSuccessStatusCode();

        using var response = await UnassignAsync(_norte.Id, chief.Id);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await _host.AssignedComparsasAsync(chief.Id));
    }

    [Fact]
    public async Task An_admin_removes_an_assignment_and_it_is_audited()
    {
        var chief = await _host.CreateUserAsync("jefe.retirado@example.test");
        using var assigned = await AssignAsync(_norte.Id, chief.Id);

        using var response = await UnassignAsync(_norte.Id, chief.Id);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await _host.AssignedComparsasAsync(chief.Id));
        var entry = Assert.Single(await _host.AuditEntriesAsync("FiringChiefUnassigned"));
        Assert.Equal((_adminUser.Id, _norte.Id, chief.Id.ToString()), (entry.ActorUserId, entry.ComparsaId, entry.EntityId));
    }

    [Fact]
    public async Task Assignments_can_be_removed_from_deactivated_users_and_inactive_comparsas()
    {
        var chief = await _host.CreateUserAsync("jefe.saliente@example.test", active: false);
        await _host.AssignAsync(_norte.Id, chief.Id);
        using var deactivated = await _admin.PostAsync($"/api/comparsas/{_norte.Id}/deactivate", new { });
        deactivated.EnsureSuccessStatusCode();

        using var response = await UnassignAsync(_norte.Id, chief.Id);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await _host.AssignedComparsasAsync(chief.Id));
    }

    [Fact]
    public async Task A_firing_chief_cannot_manage_assignments_even_for_their_own_comparsa()
    {
        var chief = await _host.CreateUserAsync("jefe.propio@example.test");
        var colleague = await _host.CreateUserAsync("jefe.colega@example.test");
        await _host.AssignAsync(_norte.Id, chief.Id);
        using var client = await _host.SignInAsync(chief);

        using var assign = await client.PutAsync($"/api/comparsas/{_norte.Id}/firing-chiefs/{colleague.Id}", null, TestContext.Current.CancellationToken);
        using var unassign = await client.DeleteAsync($"/api/comparsas/{_norte.Id}/firing-chiefs/{chief.Id}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, assign.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, unassign.StatusCode);
        Assert.Empty(await _host.AssignedComparsasAsync(colleague.Id));
        Assert.Empty(await _host.AuditEntriesAsync("FiringChiefAssigned"));
        Assert.Empty(await _host.AuditEntriesAsync("FiringChiefUnassigned"));
        Assert.Equal([_norte.Id], await _host.AssignedComparsasAsync(chief.Id));
    }

    [Fact]
    public async Task A_comparsa_lists_its_firing_chiefs_with_name_email_and_status_sorted_by_name()
    {
        var uno = await _host.CreateUserAsync("zeta.jefe@example.test");
        var dos = await _host.CreateUserAsync("alba.jefa@example.test", withPassword: false, enrolled: false);
        using var first = await AssignAsync(_norte.Id, uno.Id);
        using var second = await AssignAsync(_norte.Id, dos.Id);
        await _host.AssignAsync(_norte.Id, Guid.CreateVersion7());

        using var response = await _admin.GetAsync($"/api/comparsas/{_norte.Id}/firing-chiefs", TestContext.Current.CancellationToken);

        var chiefs = await ReadAsync<List<FiringChiefResponse>>(response);
        Assert.Equal(
            [new FiringChiefResponse(dos.Id, "Persona Sintética alba.jefa", dos.Email, UserStatus.Invited), new FiringChiefResponse(uno.Id, "Persona Sintética zeta.jefe", uno.Email, UserStatus.Active)],
            chiefs);
    }

    [Fact]
    public async Task A_firing_chief_lists_their_comparsas_including_inactive_ones_sorted_by_name()
    {
        var chief = await _host.CreateUserAsync("jefe.dos.comparsas@example.test");
        var sur = await CreateComparsaAsync("Comparsa Sintética Sur");
        using var norte = await AssignAsync(_norte.Id, chief.Id);
        using var assigned = await AssignAsync(sur.Id, chief.Id);
        using var deactivated = await _admin.PostAsync($"/api/comparsas/{sur.Id}/deactivate", new { });
        deactivated.EnsureSuccessStatusCode();

        using var response = await _admin.GetAsync($"/api/firing-chiefs/{chief.Id}/comparsas", TestContext.Current.CancellationToken);

        var comparsas = await ReadAsync<List<ComparsaResponse>>(response);
        Assert.Equal([(_norte.Id, true), (sur.Id, false)], comparsas.Select(c => (c.Id, c.Active)));
    }

    [Fact]
    public async Task Listing_for_an_unknown_comparsa_or_user_is_not_found()
    {
        using var comparsa = await _admin.GetAsync($"/api/comparsas/{Guid.CreateVersion7()}/firing-chiefs", TestContext.Current.CancellationToken);
        using var user = await _admin.GetAsync($"/api/firing-chiefs/{Guid.CreateVersion7()}/comparsas", TestContext.Current.CancellationToken);

        await AssertProblemAsync(comparsa, HttpStatusCode.NotFound, "comparsas.notFound");
        await AssertProblemAsync(user, HttpStatusCode.NotFound, "assignments.userNotFound");
    }

    [Fact]
    public async Task A_firing_chief_cannot_list_assignments()
    {
        var chief = await _host.CreateUserAsync("jefe.curioso@example.test");
        await _host.AssignAsync(_norte.Id, chief.Id);
        using var client = await _host.SignInAsync(chief);

        using var byComparsa = await client.GetAsync($"/api/comparsas/{_norte.Id}/firing-chiefs", TestContext.Current.CancellationToken);
        using var byUser = await client.GetAsync($"/api/firing-chiefs/{chief.Id}/comparsas", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, byComparsa.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, byUser.StatusCode);
    }

    private Task<HttpResponseMessage> AssignAsync(Guid comparsaId, Guid userId) =>
        _admin.PutAsync($"/api/comparsas/{comparsaId}/firing-chiefs/{userId}", null, TestContext.Current.CancellationToken);

    private Task<HttpResponseMessage> UnassignAsync(Guid comparsaId, Guid userId) =>
        _admin.DeleteAsync($"/api/comparsas/{comparsaId}/firing-chiefs/{userId}", TestContext.Current.CancellationToken);

    private async Task<ComparsaResponse> CreateComparsaAsync(string name)
    {
        using var response = await _admin.PostAsync("/api/comparsas", new { name, side = "CHRISTIAN" });
        return await ReadAsync<ComparsaResponse>(response);
    }
}
