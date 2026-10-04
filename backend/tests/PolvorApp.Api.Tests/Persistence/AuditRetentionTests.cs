using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.AuditPrivacy.Retention;
using PolvorApp.SharedKernel.Modules;

namespace PolvorApp.Api.Tests.Persistence;

/// <summary>Spec audit-privacy "Audit retention" and design D3: the settings and the start-up checks.</summary>
public sealed class AuditRetentionOptionsTests
{
    [Fact]
    public async Task The_defaults_are_five_years_and_a_year()
    {
        await using var factory = new ApiFactory("Host=offline");

        var options = factory.Services.GetRequiredService<IOptions<AuditRetentionOptions>>().Value;

        Assert.Equal((5, 365), (options.RetentionYears, options.SecurityRetentionDays));
    }

    [Theory]
    [InlineData("Audit:RetentionYears", "2", "Audit:RetentionYears")]
    [InlineData("Audit:SecurityRetentionDays", "30", "Audit:SecurityRetentionDays")]
    [InlineData("Audit:RetentionYears", "5.5", "Audit:RetentionYears")]
    [InlineData("Audit:SecurityRetentionDays", "un año", "Audit:SecurityRetentionDays")]
    [InlineData("Audit:PurgeEnabled", "quizá", "Audit:PurgeEnabled")]
    [InlineData("Audit:RetentionYears", "101", "Audit:RetentionYears")]
    [InlineData("Audit:SecurityRetentionDays", "2147483647", "Audit:SecurityRetentionDays")]
    public async Task Start_up_refuses_an_invalid_period_naming_the_setting_only(string key, string value, string named)
    {
        await using var factory = new ApiFactory("Host=offline", settings: new Dictionary<string, string?> { [key] = value });

        var exception = factory.StartupFailure<OptionsValidationException>();

        Assert.Contains(named, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain($"'{value}'", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain($" {value} ", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_purge_runs_as_a_hosted_service_and_a_host_command()
    {
        await using var factory = new ApiFactory("Host=offline");

        Assert.Contains(factory.Services.GetServices<IHostedService>(), s => s is AuditRetentionService);
        Assert.Contains(factory.Services.GetServices<IHostCommand>(), c => c.Verb == "purge-audit");
    }
}

/// <summary>Spec audit-privacy "Audit retention": what the daily purge deletes and records.</summary>
public sealed class AuditPurgeTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2031, 6, 1, 3, 0, 0, TimeSpan.Zero);
    private readonly FakeTimeProvider _time = new(Now);
    private ApiFactory? _factory;
    private NpgsqlDataSource _dataSource = null!;
    private string _connectionString = null!;

    public async ValueTask InitializeAsync()
    {
        _connectionString = await postgres.CreateMigratedDatabaseAsync();
        _dataSource = NpgsqlDataSource.Create(_connectionString);
        _factory = new ApiFactory(_connectionString, configureServices: services => services.AddSingleton<TimeProvider>(_time));
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
    public async Task Security_events_go_after_a_year_and_the_rest_after_five()
    {
        await InsertAsync("SignedIn", Now.AddDays(-366));
        await InsertAsync("SignInFailed", Now.AddDays(-364));
        await InsertAsync("ExportDownloaded", Now.AddYears(-5).AddDays(-1));
        await InsertAsync("ExportDownloaded", Now.AddYears(-5).AddDays(1));
        await InsertAsync("ArquebusierUpdated", Now.AddYears(-4));
        await InsertAsync("RenamedLongAgo", Now.AddDays(-400));

        var result = await PurgeAsync();

        Assert.Equal((1, 1), (result.Security, result.Standard));
        Assert.Equal(
            ["ArquebusierUpdated", "AuditEntriesPurged", "ExportDownloaded", "RenamedLongAgo", "SignInFailed"],
            await ActionsAsync());
        Assert.Equal("{\"security\": 1, \"standard\": 1}", await ScalarAsync("SELECT data::text FROM audit.audit_entries WHERE action = 'AuditEntriesPurged'"));
        Assert.Equal(DBNull.Value, await ScalarAsync("SELECT actor_user_id FROM audit.audit_entries WHERE action = 'AuditEntriesPurged'"));
    }

    /// <summary>Every security code of the catalogue (design D2) is kept a year, not five.</summary>
    [Theory]
    [InlineData("SignedIn")]
    [InlineData("SignInFailed")]
    [InlineData("LockedOut")]
    [InlineData("RecoveryCodeUsed")]
    [InlineData("PasswordResetRequested")]
    [InlineData("AdminBootstrapRefused")]
    [InlineData("LoanLenderLookedUp")]
    [InlineData("PersonLookedUp")]
    public async Task Each_security_event_goes_after_a_year(string action)
    {
        await InsertAsync(action, Now.AddDays(-366));
        await InsertAsync(action, Now.AddDays(-300));

        var result = await PurgeAsync();

        Assert.Equal((1, 0), (result.Security, result.Standard));
        Assert.Equal(new[] { "AuditEntriesPurged", action }.Order(StringComparer.Ordinal), await ActionsAsync());
    }

    [Fact]
    public async Task An_entry_exactly_at_the_cut_off_is_kept()
    {
        await InsertAsync("SignedIn", Now.AddDays(-365));

        var result = await PurgeAsync();

        Assert.Equal(0, result.Total);
        Assert.Equal(["SignedIn"], await ActionsAsync());
    }

    [Fact]
    public async Task Configured_periods_are_honoured()
    {
        await using var factory = new ApiFactory(
            _connectionString,
            settings: new Dictionary<string, string?> { ["Audit:RetentionYears"] = "7", ["Audit:SecurityRetentionDays"] = "400" },
            configureServices: services => services.AddSingleton<TimeProvider>(_time));
        await InsertAsync("SignedIn", Now.AddDays(-399));
        await InsertAsync("SignInFailed", Now.AddDays(-401));
        await InsertAsync("ExportDownloaded", Now.AddYears(-6));
        await InsertAsync("ArquebusierDeleted", Now.AddYears(-7).AddDays(-1));

        await using var scope = factory.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<AuditPurge>().RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal((1, 1), (result.Security, result.Standard));
        Assert.Equal(["AuditEntriesPurged", "ExportDownloaded", "SignedIn"], await ActionsAsync());
    }

    [Fact]
    public async Task Exactly_one_batch_is_deleted_whole()
    {
        await InsertManyAsync("SignedIn", AuditPurge.BatchSize);

        var result = await PurgeAsync();

        Assert.Equal(AuditPurge.BatchSize, result.Security);
        Assert.Equal(["AuditEntriesPurged"], await ActionsAsync());
    }

    [Fact]
    public async Task Nothing_expired_deletes_and_records_nothing()
    {
        await InsertAsync("SignedIn", Now.AddDays(-10));

        var result = await PurgeAsync();

        Assert.Equal(0, result.Total);
        Assert.Equal(["SignedIn"], await ActionsAsync());
    }

    [Fact]
    public async Task Large_backlogs_are_deleted_in_batches()
    {
        await InsertManyAsync("SignedIn", AuditPurge.BatchSize + 7);

        var result = await PurgeAsync();

        Assert.Equal(AuditPurge.BatchSize + 7, result.Security);
        Assert.Equal(["AuditEntriesPurged"], await ActionsAsync());
    }

    [Fact]
    public async Task The_service_purges_a_minute_after_start_up_and_then_daily()
    {
        await using var factory = new ApiFactory(
            _connectionString,
            settings: new Dictionary<string, string?> { ["Audit:PurgeEnabled"] = "true" },
            configureServices: services => services.AddSingleton<TimeProvider>(_time));
        _ = factory.Services; // starts the host and the service's first wait
        await InsertAsync("SignedIn", Now.AddDays(-500));

        _time.Advance(AuditRetentionService.FirstDelay);
        await WaitForPurgesAsync(1);
        await InsertAsync("SignedIn", Now.AddDays(-500));
        _time.Advance(AuditRetentionService.Interval);
        await WaitForPurgesAsync(2);

        Assert.Equal(["AuditEntriesPurged", "AuditEntriesPurged"], await ActionsAsync());
    }

    [Fact]
    public async Task The_host_command_runs_the_purge()
    {
        await InsertAsync("SignedIn", Now.AddDays(-500));
        var command = _factory!.Services.GetServices<IHostCommand>().Single(c => c.Verb == "purge-audit");

        var exitCode = await command.RunAsync([], TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        Assert.Equal(["AuditEntriesPurged"], await ActionsAsync());
    }

    /// <summary>Waits for the background service to have recorded <paramref name="purges"/> purges.</summary>
    private async Task WaitForPurgesAsync(int purges)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if ((await ActionsAsync()).Count(a => a == "AuditEntriesPurged") >= purges)
            {
                return;
            }

            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        Assert.Fail($"The service did not record purge {purges}.");
    }

    private async Task<AuditPurgeResult> PurgeAsync()
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AuditPurge>().RunAsync(TestContext.Current.CancellationToken);
    }

    private async Task InsertManyAsync(string action, int count)
    {
        await using var command = _dataSource.CreateCommand(
            "INSERT INTO audit.audit_entries (id, occurred_at, action, entity_type) "
            + "SELECT gen_random_uuid(), $1::timestamptz - make_interval(days => 400 + n % 30), $2, 'User' FROM generate_series(1, $3) AS n");
        command.Parameters.Add(new NpgsqlParameter { Value = Now.UtcDateTime, NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.TimestampTz });
        command.Parameters.Add(new NpgsqlParameter { Value = action });
        command.Parameters.Add(new NpgsqlParameter { Value = count });
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private async Task InsertAsync(string action, DateTimeOffset occurredAt)
    {
        await using var command = _dataSource.CreateCommand(
            "INSERT INTO audit.audit_entries (id, occurred_at, action, entity_type) VALUES (gen_random_uuid(), $1, $2, 'Widget')");
        command.Parameters.Add(new NpgsqlParameter { Value = occurredAt.UtcDateTime, NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.TimestampTz });
        command.Parameters.Add(new NpgsqlParameter { Value = action });
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private async Task<List<string>> ActionsAsync()
    {
        await using var command = _dataSource.CreateCommand("SELECT action FROM audit.audit_entries ORDER BY action");
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        var actions = new List<string>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            actions.Add(reader.GetString(0));
        }

        return actions;
    }

    private async Task<object?> ScalarAsync(string sql)
    {
        await using var command = _dataSource.CreateCommand(sql);
        return await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
    }
}
