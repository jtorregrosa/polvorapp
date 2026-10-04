using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.AuditPrivacy.Viewer;

namespace PolvorApp.Api.Tests.Audit;

/// <summary>Spec audit-privacy "Audit log query (UC-25)" and design D10: filters and keyset paging on the database.</summary>
public sealed class AuditQueryTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateTimeOffset T0 = new(2030, 3, 10, 9, 0, 0, TimeSpan.Zero);
    private readonly Dictionary<Guid, (DateTimeOffset, Guid)> _order = [];
    private ApiFactory? _factory;
    private NpgsqlDataSource _dataSource = null!;

    public async ValueTask InitializeAsync()
    {
        var connectionString = await postgres.CreateMigratedDatabaseAsync();
        _dataSource = NpgsqlDataSource.Create(connectionString);
        _factory = new ApiFactory(connectionString);
    }

    public async ValueTask DisposeAsync()
    {
        await _dataSource.DisposeAsync();
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
    }

    [Fact]
    public async Task Pages_follow_each_other_without_repeating_or_skipping_also_on_ties()
    {
        var expected = new List<Guid>();
        for (var i = 0; i < 7; i++)
        {
            // Pairs of entries share an instant, so the id breaks the tie.
            expected.Add(await InsertAsync("ArquebusierUpdated", T0.AddMinutes(i / 2)));
        }

        var seen = new List<Guid>();
        AuditCursor? cursor = null;
        do
        {
            var page = await ReadAsync(AuditFilter.Create(limit: 2, cursor: cursor));
            seen.AddRange(page.Entries.Select(e => e.Id));
            await InsertAsync("ArquebusierUpdated", T0.AddDays(1)); // written meanwhile, newer than every page
            cursor = page.Next;
        }
        while (cursor is not null);

        // The entries written while paging are newer than the first page, so no later page shows them.
        Assert.Equal(expected.OrderByDescending(id => _order[id]).ToList(), seen);
    }

    [Fact]
    public async Task The_last_page_has_no_cursor()
    {
        await InsertAsync("ArquebusierUpdated", T0);

        var page = await ReadAsync(AuditFilter.Create(limit: 1));

        Assert.Single(page.Entries);
        Assert.Null(page.Next);
    }

    [Fact]
    public async Task Filters_combine()
    {
        var actor = Guid.CreateVersion7();
        var comparsa = Guid.CreateVersion7();
        var match = await InsertAsync("ArquebusierUpdated", T0, actor, comparsa, "Arquebusier", "a1");
        await InsertAsync("ArquebusierUpdated", T0, actor, Guid.CreateVersion7(), "Arquebusier", "a1");
        await InsertAsync("ArquebusierUpdated", T0, null, comparsa, "Arquebusier", "a1");
        await InsertAsync("ComparsaOrderValidated", T0, actor, comparsa, "ComparsaOrder", "o1");
        await InsertAsync("ArquebusierUpdated", T0.AddDays(-40), actor, comparsa, "Arquebusier", "a1");

        var page = await ReadAsync(AuditFilter.Create(
            from: T0.AddDays(-1),
            before: T0.AddDays(1),
            actor: new AuditActorFilter.User(actor),
            comparsaId: comparsa,
            entity: new AuditEntityFilter("Arquebusier", "a1"),
            action: "ArquebusierUpdated"));

        Assert.Equal([match], page.Entries.Select(e => e.Id));
    }

    [Fact]
    public async Task Entries_without_a_user_can_be_asked_for()
    {
        var anonymous = await InsertAsync("SignInFailed", T0);
        await InsertAsync("SignedIn", T0, Guid.CreateVersion7());

        var page = await ReadAsync(AuditFilter.Create(actor: AuditActorFilter.None));

        Assert.Equal([anonymous], page.Entries.Select(e => e.Id));
    }

    [Fact]
    public async Task The_period_includes_its_start_and_excludes_the_next_day()
    {
        var first = await InsertAsync("SignedIn", T0);
        await InsertAsync("SignedIn", T0.AddDays(1));

        var page = await ReadAsync(AuditFilter.Create(from: T0, before: T0.AddDays(1)));

        Assert.Equal([first], page.Entries.Select(e => e.Id));
    }

    private async Task<AuditPage> ReadAsync(AuditFilter filter)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        return await ActivatorUtilities.CreateInstance<AuditQuery>(scope.ServiceProvider).ReadAsync(filter, TestContext.Current.CancellationToken);
    }

    private async Task<Guid> InsertAsync(
        string action, DateTimeOffset occurredAt, Guid? actor = null, Guid? comparsa = null, string entityType = "Widget", string? entityId = null)
    {
        var id = Guid.CreateVersion7();
        await using var command = _dataSource.CreateCommand(
            "INSERT INTO audit.audit_entries (id, occurred_at, action, entity_type, entity_id, actor_user_id, comparsa_id) VALUES ($1, $2, $3, $4, $5, $6, $7)");
        command.Parameters.Add(new NpgsqlParameter { Value = id });
        command.Parameters.Add(new NpgsqlParameter { Value = occurredAt.UtcDateTime, NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.TimestampTz });
        command.Parameters.Add(new NpgsqlParameter { Value = action });
        command.Parameters.Add(new NpgsqlParameter { Value = entityType });
        command.Parameters.Add(new NpgsqlParameter { Value = (object?)entityId ?? DBNull.Value, NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Varchar });
        command.Parameters.Add(new NpgsqlParameter { Value = (object?)actor ?? DBNull.Value, NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Uuid });
        command.Parameters.Add(new NpgsqlParameter { Value = (object?)comparsa ?? DBNull.Value, NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Uuid });
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        _order[id] = (occurredAt, id);
        return id;
    }
}
