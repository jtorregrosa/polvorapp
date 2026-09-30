using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.FederationCatalog.Endpoints;
using PolvorApp.IdentityAccess.Contracts;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Catalog;

/// <summary>Spec "Deleting comparsas and weapon models" (comparsa part) and design D10.</summary>
[Collection(PostgresGroup.Name)]
public sealed class ComparsaDeletionTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private readonly FakeCatalogUsage _usage = new();
    private IdentityTestHost _host = null!;
    private SyntheticUser _adminUser = null!;
    private HttpClient _admin = null!;

    public async ValueTask InitializeAsync()
    {
        _host = await IdentityTestHost.StartAsync(postgres, mailpit, configureServices: services => services.AddSingleton<ICatalogUsage>(_usage));
        _adminUser = await _host.CreateUserAsync("admin.borrado@example.test", UserRole.Admin);
        _admin = await _host.SignInAsync(_adminUser);
    }

    public async ValueTask DisposeAsync()
    {
        _admin.Dispose();
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task An_unused_comparsa_is_deleted_and_its_name_can_be_reused()
    {
        var comparsa = await CreateAsync("Comparsa Sintética Efímera");

        using var deleted = await _admin.DeleteAsync($"/api/comparsas/{comparsa.Id}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        using var gone = await _admin.GetAsync($"/api/comparsas/{comparsa.Id}", TestContext.Current.CancellationToken);
        await AssertProblemAsync(gone, HttpStatusCode.NotFound, "comparsas.notFound");
        Assert.NotEqual(comparsa.Id, (await CreateAsync("comparsa sintética efímera")).Id);
    }

    [Fact]
    public async Task Deleting_a_comparsa_removes_its_assignments_and_audits_a_snapshot()
    {
        var comparsa = await CreateAsync("Comparsa Sintética Disuelta");
        var other = await CreateAsync("Comparsa Sintética Superviviente");
        var (uno, dos) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        await _host.AssignAsync(comparsa.Id, uno);
        await _host.AssignAsync(comparsa.Id, dos);
        await _host.AssignAsync(other.Id, dos);

        using var deleted = await _admin.DeleteAsync($"/api/comparsas/{comparsa.Id}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Empty(await _host.AssignedComparsasAsync(uno));
        Assert.Equal([other.Id], await _host.AssignedComparsasAsync(dos));
        var entry = Assert.Single(await _host.AuditEntriesAsync("ComparsaDeleted"));
        Assert.Equal((_adminUser.Id, comparsa.Id, comparsa.Id.ToString()), (entry.ActorUserId, entry.ComparsaId, entry.EntityId));
        Assert.Contains("Comparsa Sintética Disuelta", entry.Data, StringComparison.Ordinal);
        Assert.Contains("\"CHRISTIAN\"", entry.Data, StringComparison.Ordinal);
        using var snapshot = JsonDocument.Parse(entry.Data!);
        Assert.True(snapshot.RootElement.GetProperty("active").GetBoolean());
        var unassigned = snapshot.RootElement.GetProperty("unassignedUserIds").EnumerateArray().Select(e => e.GetGuid()).Order();
        Assert.Equal(new[] { uno, dos }.Order(), unassigned);
    }

    [Fact]
    public async Task The_row_lock_blocks_a_concurrent_assignment_so_the_snapshot_misses_nothing()
    {
        var comparsa = await CreateAsync("Comparsa Sintética Bloqueada");
        var late = Guid.CreateVersion7();
        Task? assignment = null;
        _usage.DuringComparsaCheck = async (id, cancellationToken) =>
        {
            assignment = _host.AssignAsync(id, late);
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
            Assert.False(assignment.IsCompleted, "The assignment should wait for the deletion's row lock.");
        };

        using var deleted = await _admin.DeleteAsync($"/api/comparsas/{comparsa.Id}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => assignment!);
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
        Assert.Empty(await _host.AssignedComparsasAsync(late));
        Assert.DoesNotContain(late.ToString(), Assert.Single(await _host.AuditEntriesAsync("ComparsaDeleted")).Data, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_comparsa_in_use_is_not_deleted()
    {
        var comparsa = await CreateAsync("Comparsa Sintética Ocupada");
        var chief = Guid.CreateVersion7();
        await _host.AssignAsync(comparsa.Id, chief);
        _usage.ComparsasInUse.Add(comparsa.Id);

        using var response = await _admin.DeleteAsync($"/api/comparsas/{comparsa.Id}", TestContext.Current.CancellationToken);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "comparsas.inUse");
        using var still = await _admin.GetAsync($"/api/comparsas/{comparsa.Id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, still.StatusCode);
        Assert.Equal([comparsa.Id], await _host.AssignedComparsasAsync(chief));
        Assert.Empty(await _host.AuditEntriesAsync("ComparsaDeleted"));
    }

    [Fact]
    public async Task Deleting_an_unknown_comparsa_is_not_found()
    {
        using var response = await _admin.DeleteAsync($"/api/comparsas/{Guid.CreateVersion7()}", TestContext.Current.CancellationToken);

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "comparsas.notFound");
    }

    [Fact]
    public async Task A_firing_chief_cannot_delete_a_comparsa()
    {
        var comparsa = await CreateAsync("Comparsa Sintética Blindada");
        using var chief = await _host.SignInAsync(await _host.CreateUserAsync("jefe.borrado@example.test"));

        using var response = await chief.DeleteAsync($"/api/comparsas/{comparsa.Id}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(await _host.AuditEntriesAsync("ComparsaDeleted"));
    }

    private async Task<ComparsaResponse> CreateAsync(string name)
    {
        using var response = await _admin.PostAsync("/api/comparsas", new { name, side = "CHRISTIAN" });
        return await ReadAsync<ComparsaResponse>(response);
    }
}
