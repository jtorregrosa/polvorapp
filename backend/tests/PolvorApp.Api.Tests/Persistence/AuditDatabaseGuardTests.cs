using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.AuditPrivacy.Maintenance;
using PolvorApp.AuditPrivacy.Persistence;

namespace PolvorApp.Api.Tests.Persistence;

/// <summary>
/// Spec audit-privacy "Audit trail is append-only" and design D4, D9, D10: the database refuses every
/// change to audit entries outside the two maintenance modes, and the migration cleans the names that
/// <c>UserUpdated</c> used to record.
/// </summary>
public sealed class AuditDatabaseGuardTests(PostgresFixture postgres) : IAsyncLifetime
{
    private NpgsqlDataSource _dataSource = null!;

    public async ValueTask InitializeAsync() =>
        _dataSource = NpgsqlDataSource.Create(await postgres.CreateMigratedDatabaseAsync());

    public async ValueTask DisposeAsync() => await _dataSource.DisposeAsync();

    [Fact]
    public async Task An_update_outside_maintenance_is_refused()
    {
        var id = await InsertAsync("WidgetCreated");

        var error = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(null, $"UPDATE audit.audit_entries SET action = 'Tampered' WHERE id = '{id}'"));

        Assert.Equal(AuditMaintenance.GuardSqlState, error.SqlState);
        Assert.Equal("WidgetCreated", await ActionOfAsync(id));
    }

    [Fact]
    public async Task A_delete_outside_maintenance_is_refused()
    {
        var id = await InsertAsync("WidgetCreated");

        await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(null, $"DELETE FROM audit.audit_entries WHERE id = '{id}'"));

        Assert.Equal("WidgetCreated", await ActionOfAsync(id));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("purge")]
    [InlineData("redact")]
    public async Task Truncate_is_always_refused(string? mode)
    {
        var id = await InsertAsync("WidgetCreated");

        await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(mode, "TRUNCATE audit.audit_entries"));

        Assert.Equal("WidgetCreated", await ActionOfAsync(id));
    }

    [Fact]
    public async Task Purge_mode_deletes_but_cannot_update()
    {
        var deleted = await InsertAsync("SignedIn");
        var kept = await InsertAsync("SignedIn");

        await ExecuteAsync("purge", $"DELETE FROM audit.audit_entries WHERE id = '{deleted}'");
        await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync("purge", $"UPDATE audit.audit_entries SET data = '{{}}' WHERE id = '{kept}'"));

        Assert.Null(await ActionOfAsync(deleted));
        Assert.Equal("SignedIn", await ActionOfAsync(kept));
    }

    [Fact]
    public async Task Redact_mode_changes_data_only()
    {
        var id = await InsertAsync("SignInFailed", """{"attemptedEmail":"nadie@example.test","step":"password"}""");

        await ExecuteAsync("redact", $"UPDATE audit.audit_entries SET data = jsonb_set(data, '{{attemptedEmail}}', '\"[erased]\"') WHERE id = '{id}'");
        await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync("redact", $"UPDATE audit.audit_entries SET action = 'Tampered' WHERE id = '{id}'"));
        await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync("redact", $"UPDATE audit.audit_entries SET occurred_at = now() - interval '1 day' WHERE id = '{id}'"));
        await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync("redact", $"DELETE FROM audit.audit_entries WHERE id = '{id}'"));

        Assert.Equal("SignInFailed", await ActionOfAsync(id));
        Assert.Equal("[erased]", await ScalarAsync<string>($"SELECT data->>'attemptedEmail' FROM audit.audit_entries WHERE id = '{id}'"));
    }

    [Fact]
    public async Task Redact_mode_refuses_data_changed_together_with_another_column()
    {
        var id = await InsertAsync("SignInFailed", """{"attemptedEmail":"nadie@example.test"}""");

        await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            "redact", $"UPDATE audit.audit_entries SET data = '{{}}', entity_id = 'x' WHERE id = '{id}'"));

        Assert.Equal("nadie@example.test", await ScalarAsync<string>($"SELECT data->>'attemptedEmail' FROM audit.audit_entries WHERE id = '{id}'"));
    }

    [Fact]
    public async Task An_upsert_cannot_overwrite_an_entry()
    {
        var id = await InsertAsync("WidgetCreated");

        await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            null,
            $"INSERT INTO audit.audit_entries (id, occurred_at, action, entity_type) VALUES ('{id}', now(), 'Tampered', 'Widget') "
            + "ON CONFLICT (id) DO UPDATE SET action = EXCLUDED.action"));

        Assert.Equal("WidgetCreated", await ActionOfAsync(id));
    }

    [Fact]
    public async Task The_guard_also_fires_in_replica_mode()
    {
        var id = await InsertAsync("WidgetCreated");

        await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            null, $"SET LOCAL session_replication_role = replica; DELETE FROM audit.audit_entries WHERE id = '{id}'"));

        Assert.Equal("WidgetCreated", await ActionOfAsync(id));
    }

    [Fact]
    public async Task An_unknown_mode_is_refused()
    {
        var id = await InsertAsync("SignedIn");

        await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync("anything", $"DELETE FROM audit.audit_entries WHERE id = '{id}'"));

        Assert.Equal("SignedIn", await ActionOfAsync(id));
    }

    [Fact]
    public async Task The_mode_ends_with_its_transaction()
    {
        var id = await InsertAsync("SignedIn");
        await using var connection = await _dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using (var transaction = await connection.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            await RunAsync(connection, transaction, "SELECT set_config('polvorapp.audit_maintenance', 'purge', true)");
            await transaction.CommitAsync(TestContext.Current.CancellationToken);
        }

        await Assert.ThrowsAsync<PostgresException>(() => RunAsync(connection, null, $"DELETE FROM audit.audit_entries WHERE id = '{id}'"));

        Assert.Equal("SignedIn", await ActionOfAsync(id));
    }

    [Fact]
    public async Task Inserting_still_works()
    {
        var id = await InsertAsync("WidgetCreated");

        Assert.Equal("WidgetCreated", await ActionOfAsync(id));
    }

    [Fact]
    public async Task The_viewer_index_orders_by_time_and_id()
    {
        var indexes = await ScalarsAsync("SELECT indexname FROM pg_indexes WHERE schemaname = 'audit' AND tablename = 'audit_entries'");

        Assert.Contains("ix_audit_entries_occurred_at_id", indexes);
        Assert.Contains("ix_audit_entries_action_occurred_at_id", indexes);
        Assert.Contains("ix_audit_entries_actor_user_id_occurred_at_id", indexes);
        Assert.Contains("ix_audit_entries_comparsa_id_occurred_at_id", indexes);
        Assert.Contains("ix_audit_entries_entity_type_entity_id_occurred_at_id", indexes);
        Assert.DoesNotContain("ix_audit_entries_occurred_at", indexes);
    }

    [Fact]
    public async Task The_migration_removes_names_from_user_updates()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var factory = new ApiFactory(connectionString);
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
            await db.GetService<IMigrator>().MigrateAsync("20260930141315_InitialAuditTrail", TestContext.Current.CancellationToken);
            await using (var insert = dataSource.CreateCommand(
                """
                INSERT INTO audit.audit_entries (id, occurred_at, action, entity_type, entity_id, data) VALUES
                ('01900000-0000-7000-8000-000000000001', now(), 'UserUpdated', 'User', 'u1',
                 '{"previous":{"name":"Nombre Sintético","role":"FIRING_CHIEF","locale":"es-ES"},"current":{"name":"Nombre Cambiado","role":"ADMIN","locale":"es-ES"}}'),
                ('01900000-0000-7000-8000-000000000002', now(), 'ComparsaUpdated', 'Comparsa', 'c1',
                 '{"previous":{"name":"Comparsa Sintética Norte"},"current":{"name":"Comparsa Sintética Sur"}}')
                """))
            {
                await insert.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            }

            await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
        }

        await using var command = dataSource.CreateCommand("SELECT action, data::text FROM audit.audit_entries ORDER BY id");
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
        var user = reader.GetString(1);
        Assert.DoesNotContain("Nombre", user, StringComparison.Ordinal);
        Assert.Contains("\"role\": \"ADMIN\"", user, StringComparison.Ordinal);
        Assert.Contains("\"role\": \"FIRING_CHIEF\"", user, StringComparison.Ordinal);
        Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
        Assert.Contains("Comparsa Sintética Sur", reader.GetString(1), StringComparison.Ordinal);
    }

    private async Task<Guid> InsertAsync(string action, string? data = null)
    {
        var id = Guid.CreateVersion7();
        await using var command = _dataSource.CreateCommand(
            "INSERT INTO audit.audit_entries (id, occurred_at, action, entity_type, data) VALUES ($1, now(), $2, 'Widget', $3::jsonb)");
        command.Parameters.Add(new NpgsqlParameter { Value = id });
        command.Parameters.Add(new NpgsqlParameter { Value = action });
        command.Parameters.Add(new NpgsqlParameter { Value = (object?)data ?? DBNull.Value, NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text });
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        return id;
    }

    /// <summary>Runs <paramref name="sql"/> in a transaction of its own, in the given maintenance mode (none when null).</summary>
    private async Task ExecuteAsync(string? mode, string sql)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(TestContext.Current.CancellationToken);
        if (mode is not null)
        {
            await RunAsync(connection, transaction, $"SELECT set_config('polvorapp.audit_maintenance', '{mode}', true)");
        }

        await RunAsync(connection, transaction, sql);
        await transaction.CommitAsync(TestContext.Current.CancellationToken);
    }

    private static async Task RunAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private Task<string?> ActionOfAsync(Guid id) => ScalarAsync<string>($"SELECT action FROM audit.audit_entries WHERE id = '{id}'");

    private async Task<T?> ScalarAsync<T>(string sql)
    {
        await using var command = _dataSource.CreateCommand(sql);
        var value = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        return value is null or DBNull ? default : (T)value;
    }

    private async Task<List<string>> ScalarsAsync(string sql)
    {
        await using var command = _dataSource.CreateCommand(sql);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        var values = new List<string>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }
}
