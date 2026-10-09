using Docker.DotNet.Models;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PolvorApp.SharedKernel.Persistence;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(PolvorApp.Api.Tests.Infrastructure.PostgresFixture))]

namespace PolvorApp.Api.Tests.Infrastructure;

/// <summary>
/// One disposable PostgreSQL container shared by every test in the assembly. Test classes run in
/// parallel: each test works on a database of its own (<see cref="CreateMigratedDatabaseAsync"/>,
/// <see cref="CreateDatabaseAsync"/>); the default database is only read.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    /// <summary>A copy of the freshly migrated default database that test databases are cloned from.</summary>
    private const string Template = "polvorapp_template";

    private const long SharedMemoryBytes = 512L * 1024 * 1024;

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18.6-alpine")
        .WithDatabase("polvorapp")
        .WithUsername("polvorapp")
        .WithPassword("test-only-password")
        // Test classes run in parallel, each host with its own connection pool.
        .WithCommand("-c", "max_connections=500")
        // Docker's default 64 MB /dev/shm runs out under that load: parallel queries then fail with
        // 53100 "could not resize shared memory segment".
        .WithCreateParameterModifier(parameters =>
        {
            parameters.HostConfig ??= new HostConfig();
            parameters.HostConfig.ShmSize = SharedMemoryBytes;
        })
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    /// <summary>The maintenance database: databases are created from here, connected to none of them.</summary>
    private string AdminConnectionString =>
        new NpgsqlConnectionStringBuilder(ConnectionString) { Database = "postgres", Pooling = false }.ConnectionString;

    /// <summary>
    /// Starts the container and applies every module's migrations to its default database, then
    /// copies it as the template of every migrated test database. The migrators run directly, not
    /// through the migrate command: that also creates the bucket, and fixtures start in no set order,
    /// so MinIO may not be up yet. Test hosts run the full migrate command on their own database.
    /// </summary>
    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        await using (var factory = new ApiFactory(ConnectionString))
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            foreach (var migrator in scope.ServiceProvider.GetServices<IDatabaseMigrator>().OrderBy(m => m.Order))
            {
                await migrator.MigrateAsync(scope.ServiceProvider, CancellationToken.None);
            }
        }

        // A database is copied only while nobody is connected to it: disposing the factory above closed
        // the migration's connections (its data source).
        await ExecuteAsync($"CREATE DATABASE {Template} TEMPLATE polvorapp");
        await ExecuteAsync($"ALTER DATABASE {Template} ALLOW_CONNECTIONS false");
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    /// <summary>Creates an empty database in the shared container and returns its connection string.</summary>
    public Task<string> CreateDatabaseAsync() => CreateAsync("CREATE DATABASE {0}");

    /// <summary>
    /// Creates a database with every migration applied, as a copy of the template (far faster than
    /// migrating), and returns its connection string. Running the migrate command on it is a no-op.
    /// </summary>
    public Task<string> CreateMigratedDatabaseAsync() => CreateAsync($"CREATE DATABASE {{0}} TEMPLATE {Template}");

    private async Task<string> CreateAsync(string statement)
    {
        var database = $"test_{Guid.NewGuid():N}";
        await ExecuteAsync(string.Format(System.Globalization.CultureInfo.InvariantCulture, statement, database));
        return new NpgsqlConnectionStringBuilder(ConnectionString) { Database = database }.ConnectionString;
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var admin = new NpgsqlConnection(AdminConnectionString);
        await admin.OpenAsync();
        await using var command = new NpgsqlCommand(sql, admin);
        await command.ExecuteNonQueryAsync();
    }
}
