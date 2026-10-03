using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using PolvorApp.Api.Platform.Database;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.SharedKernel.Persistence;

namespace PolvorApp.Api.Tests;

/// <summary>Spec platform: "Local environment with one command" — migrations run by an explicit command.</summary>
public sealed class MigrateCommandTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Migrate_creates_every_module_schema_with_its_own_history_table()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var factory = new ApiFactory(connectionString);

        var exitCode = await MigrateCommand.RunAsync(factory.Services, TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        Assert.True(await TableExistsAsync(connectionString, "audit", "audit_entries"));
        Assert.True(await TableExistsAsync(connectionString, "audit", "__EFMigrationsHistory"));
        Assert.True(await TableExistsAsync(connectionString, "identity", "__EFMigrationsHistory"));
        foreach (var table in new[] { "comparsas", "firing_chief_assignments", "weapon_models", "__EFMigrationsHistory" })
        {
            Assert.True(await TableExistsAsync(connectionString, "catalog", table), table);
        }

        foreach (var table in new[] { "arquebusiers", "owned_weapons", "__EFMigrationsHistory" })
        {
            Assert.True(await TableExistsAsync(connectionString, "registry", table), table);
        }

        foreach (var table in new[] { "festival_editions", "edition_weapon_models", "calendar_milestones", "__EFMigrationsHistory" })
        {
            Assert.True(await TableExistsAsync(connectionString, "editions", table), table);
        }
    }

    [Fact]
    public void Modules_migrate_in_dependency_order()
    {
        using var factory = new ApiFactory("Host=offline");

        var order = factory.Services.GetServices<IDatabaseMigrator>().OrderBy(m => m.Order).Select(m => m.Name);

        Assert.Equal(["AuditDbContext", "IdentityAccessDbContext", "FederationCatalogDbContext", "ArquebusierRegistryDbContext", "FestivalEditionsDbContext", "ComparsaOrdersDbContext"], order);
    }

    [Fact]
    public async Task Migrate_is_idempotent()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var factory = new ApiFactory(connectionString);

        Assert.Equal(0, await MigrateCommand.RunAsync(factory.Services, TestContext.Current.CancellationToken));
        Assert.Equal(0, await MigrateCommand.RunAsync(factory.Services, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Migrate_fails_with_a_log_when_the_database_is_unreachable()
    {
        var unreachable = new NpgsqlConnectionStringBuilder(postgres.ConnectionString) { Port = 1, Timeout = 2 }.ConnectionString;
        await using var factory = new ApiFactory(unreachable);

        var exitCode = await MigrateCommand.RunAsync(factory.Services, TestContext.Current.CancellationToken);

        Assert.Equal(1, exitCode);
        Assert.Contains(factory.Logs.Entries, e => e.Level == LogLevel.Critical && e.Message.Contains("Migration failed", StringComparison.Ordinal));
    }

    private static async Task<bool> TableExistsAsync(string connectionString, string schema, string table)
    {
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using var command = dataSource.CreateCommand(
            "SELECT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = $1 AND table_name = $2)");
        command.Parameters.Add(new NpgsqlParameter { Value = schema });
        command.Parameters.Add(new NpgsqlParameter { Value = table });
        return (bool)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }
}
