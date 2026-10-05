using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using PolvorApp.Api.Platform.Seeding;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.SharedKernel.Seeding;

namespace PolvorApp.Api.Tests;

public sealed class SeedCommandTests(PostgresFixture postgres)
{
    private sealed class RecordingSeeder(int order, List<int> calls) : IDataSeeder
    {
        public int Order => order;

        public Task SeedAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            calls.Add(order);
            return Task.CompletedTask;
        }
    }

    private sealed class FailingSeeder : IDataSeeder
    {
        public int Order => 0;

        public Task SeedAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("seeder failed");
    }

    /// <summary>Writes pseudo-random rows derived only from <see cref="SyntheticData.RandomSeed"/>.</summary>
    private sealed class SyntheticRowsSeeder(NpgsqlDataSource dataSource) : IDataSeeder
    {
        public int Order => 0;

        public async Task SeedAsync(CancellationToken cancellationToken)
        {
            var random = new Random(SyntheticData.RandomSeed);
            await using var create = dataSource.CreateCommand("CREATE TABLE sample (id int PRIMARY KEY, value int NOT NULL)");
            await create.ExecuteNonQueryAsync(cancellationToken);
            for (var id = 1; id <= 20; id++)
            {
                await using var insert = dataSource.CreateCommand("INSERT INTO sample (id, value) VALUES ($1, $2)");
                insert.Parameters.Add(new NpgsqlParameter { Value = id });
                insert.Parameters.Add(new NpgsqlParameter { Value = random.Next() });
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }
        }
    }

    [Fact]
    public async Task Seeding_is_refused_in_production_without_running_any_seeder()
    {
        var calls = new List<int>();
        var logs = new CapturingLoggerProvider();
        await using var services = Services(logs, s => s.AddSingleton<IDataSeeder>(new RecordingSeeder(1, calls)));

        var exitCode = await SeedCommand.RunAsync(services, Environment(Environments.Production), TestContext.Current.CancellationToken);

        Assert.Equal(1, exitCode);
        Assert.Empty(calls);
        Assert.Contains(logs.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("Production", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Prod")]
    [InlineData("Live")]
    [InlineData("")]
    public async Task Seeding_is_refused_in_any_environment_outside_the_allow_list(string environment)
    {
        var calls = new List<int>();
        await using var services = Services(new CapturingLoggerProvider(), s => s.AddSingleton<IDataSeeder>(new RecordingSeeder(1, calls)));

        var exitCode = await SeedCommand.RunAsync(services, Environment(environment), TestContext.Current.CancellationToken);

        Assert.Equal(1, exitCode);
        Assert.Empty(calls);
    }

    [Theory]
    [InlineData("Staging")]
    [InlineData("Testing")]
    public async Task Seeding_is_allowed_in_staging_and_testing(string environment)
    {
        var calls = new List<int>();
        await using var services = Services(new CapturingLoggerProvider(), s => s.AddSingleton<IDataSeeder>(new RecordingSeeder(1, calls)));

        var exitCode = await SeedCommand.RunAsync(services, Environment(environment), TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        Assert.Equal([1], calls);
    }

    [Fact]
    public async Task An_unknown_dataset_is_refused_before_any_seeder_runs()
    {
        var calls = new List<int>();
        var logs = new CapturingLoggerProvider();
        await using var services = Services(logs, s =>
        {
            s.AddSingleton(SharedKernel.SeedDatasetTests.Configuration("huge"));
            s.AddSingleton<IDataSeeder>(new RecordingSeeder(1, calls));
        });

        var exitCode = await SeedCommand.RunAsync(services, Environment(Environments.Development), TestContext.Current.CancellationToken);

        Assert.Equal(1, exitCode);
        Assert.Empty(calls);
        Assert.Contains(logs.Entries, e => e.Level == LogLevel.Error && e.Exception?.Contains("huge", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task The_full_dataset_runs_every_seeder_and_is_logged()
    {
        var calls = new List<int>();
        var logs = new CapturingLoggerProvider();
        await using var services = Services(logs, s =>
        {
            s.AddSingleton(SharedKernel.SeedDatasetTests.Configuration("full"));
            s.AddSingleton<IDataSeeder>(new RecordingSeeder(1, calls));
        });

        var exitCode = await SeedCommand.RunAsync(services, Environment(Environments.Development), TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        Assert.Equal([1], calls);
        Assert.Contains(logs.Entries, e => e.Message.Contains("Full", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Invalid_configuration_while_resolving_seeders_fails_the_command_with_a_log()
    {
        var logs = new CapturingLoggerProvider();
        await using var services = Services(logs, s => s.AddSingleton<IDataSeeder>(_ =>
            throw new OptionsValidationException("database", typeof(object), ["The ConnectionStrings:Postgres setting is required."])));

        var exitCode = await SeedCommand.RunAsync(services, Environment(Environments.Development), TestContext.Current.CancellationToken);

        Assert.Equal(1, exitCode);
        Assert.Contains(logs.Entries, e => e.Level >= LogLevel.Error);
    }

    [Fact]
    public async Task Cancelled_seeding_fails_the_command_with_a_log()
    {
        var logs = new CapturingLoggerProvider();
        await using var services = Services(logs, s => s.AddSingleton<IDataSeeder>(new RecordingSeeder(1, [])));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var exitCode = await SeedCommand.RunAsync(services, Environment(Environments.Development), cancelled.Token);

        Assert.Equal(1, exitCode);
        Assert.Contains(logs.Entries, e => e.Level >= LogLevel.Warning && e.Message.Contains("cancel", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Seeding_in_development_runs_every_seeder_in_order()
    {
        var calls = new List<int>();
        await using var services = Services(new CapturingLoggerProvider(), s =>
        {
            s.AddSingleton<IDataSeeder>(new RecordingSeeder(20, calls));
            s.AddSingleton<IDataSeeder>(new RecordingSeeder(10, calls));
        });

        var exitCode = await SeedCommand.RunAsync(services, Environment(Environments.Development), TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        Assert.Equal([10, 20], calls);
    }

    [Fact]
    public async Task A_failing_seeder_makes_the_command_fail()
    {
        var logs = new CapturingLoggerProvider();
        await using var services = Services(logs, s => s.AddSingleton<IDataSeeder, FailingSeeder>());

        var exitCode = await SeedCommand.RunAsync(services, Environment(Environments.Development), TestContext.Current.CancellationToken);

        Assert.Equal(1, exitCode);
        Assert.Contains(logs.Entries, e => e.Level >= LogLevel.Error);
    }

    [Fact]
    public async Task Seeding_two_empty_databases_produces_identical_data()
    {
        var first = await SeedFreshDatabaseAsync();
        var second = await SeedFreshDatabaseAsync();

        Assert.Equal(20, first.Count);
        Assert.Equal(first, second);
    }

    private async Task<List<(int Id, int Value)>> SeedFreshDatabaseAsync()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using var services = Services(new CapturingLoggerProvider(), s => s.AddSingleton<IDataSeeder>(new SyntheticRowsSeeder(dataSource)));

        var exitCode = await SeedCommand.RunAsync(services, Environment(Environments.Development), TestContext.Current.CancellationToken);
        Assert.Equal(0, exitCode);

        var rows = new List<(int, int)>();
        await using var select = dataSource.CreateCommand("SELECT id, value FROM sample ORDER BY id");
        await using var reader = await select.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            rows.Add((reader.GetInt32(0), reader.GetInt32(1)));
        }

        return rows;
    }

    private static ServiceProvider Services(CapturingLoggerProvider logs, Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddProvider(logs));
        services.AddSingleton(SharedKernel.SeedDatasetTests.Configuration(null));
        configure(services);
        return services.BuildServiceProvider();
    }

    private static HostingEnvironment Environment(string name) => new()
    {
        EnvironmentName = name,
        ApplicationName = "PolvorApp.Api",
        ContentRootPath = AppContext.BaseDirectory,
        ContentRootFileProvider = new NullFileProvider(),
    };
}
