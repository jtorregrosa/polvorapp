using Npgsql;
using PolvorApp.Api.Platform.Database;
using Testcontainers.PostgreSql;

namespace PolvorApp.Api.Tests.Infrastructure;

/// <summary>One disposable PostgreSQL container shared by every test in the assembly.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18.6-alpine")
        .WithDatabase("polvorapp")
        .WithUsername("polvorapp")
        .WithPassword("test-only-password")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    /// <summary>Starts the container and migrates its default database, as compose does before the API starts.</summary>
    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        await using var factory = new ApiFactory(ConnectionString);
        var exitCode = await MigrateCommand.RunAsync(factory.Services, CancellationToken.None);
        if (exitCode != 0)
        {
            throw new InvalidOperationException("Migrating the test database failed; see the migrate command log.");
        }
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    /// <summary>Creates an empty database in the shared container and returns its connection string.</summary>
    public async Task<string> CreateDatabaseAsync()
    {
        var database = $"test_{Guid.NewGuid():N}";
        await using (var admin = NpgsqlDataSource.Create(ConnectionString))
        await using (var create = admin.CreateCommand($"CREATE DATABASE {database}"))
        {
            await create.ExecuteNonQueryAsync();
        }

        return new NpgsqlConnectionStringBuilder(ConnectionString) { Database = database }.ConnectionString;
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresGroup : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
