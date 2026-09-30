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

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();
}

[CollectionDefinition(Name)]
public sealed class PostgresGroup : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
