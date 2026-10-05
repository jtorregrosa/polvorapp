using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PolvorApp.Api.Platform.Database;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.FederationCatalog.Logos;
using PolvorApp.FederationCatalog.Persistence;

namespace PolvorApp.Api.Tests.Catalog;

/// <summary>
/// Spec "Federation settings" (add-federation-settings, design D1): the single settings row starts
/// with today's values, and the database refuses lead times out of range and blank names.
/// </summary>
public sealed class FederationSettingsDatabaseTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiFactory? _factory;

    public async ValueTask InitializeAsync()
    {
        _factory = new ApiFactory(await postgres.CreateMigratedDatabaseAsync());
        Assert.Equal(0, await MigrateCommand.RunAsync(_factory.Services, TestContext.Current.CancellationToken));
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
    }

    [Fact]
    public async Task The_migration_starts_the_row_with_todays_values()
    {
        var settings = await ReadAsync();

        Assert.Equal(
            ("Unión de Comparsas de Moros y Cristianos «Ber-Largas»", "Unió de Comparses de Moros i Cristians «Ber-Largas»", "Unión de Comparsas"),
            (settings.OfficialNameEs, settings.OfficialNameCa, settings.ShortName));
        Assert.Equal(("PolvorApp", (string?)null, (string?)null, (string?)null), (settings.SenderName, settings.ReplyTo, settings.ContactEmail, settings.Website));
        Assert.Equal((7, 7), (settings.CloseReminderLeadDays, settings.MilestoneLeadDays));
        Assert.NotEqual(0u, settings.Version);
    }

    [Theory]
    [InlineData("close_reminder_lead_days", 1, "ck_federation_settings_close_reminder_lead_days")]
    [InlineData("close_reminder_lead_days", 15, "ck_federation_settings_close_reminder_lead_days")]
    [InlineData("milestone_lead_days", 0, "ck_federation_settings_milestone_lead_days")]
    [InlineData("milestone_lead_days", 15, "ck_federation_settings_milestone_lead_days")]
    public async Task Lead_times_out_of_range_are_refused(string column, int days, string constraint)
    {
        var error = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync($"UPDATE catalog.federation_settings SET {column} = {days}"));

        Assert.Equal((PostgresErrorCodes.CheckViolation, constraint), (error.SqlState, error.ConstraintName));
    }

    [Theory]
    [InlineData("official_name_es")]
    [InlineData("official_name_ca")]
    [InlineData("short_name")]
    [InlineData("sender_name")]
    public async Task Blank_names_are_refused(string column)
    {
        var error = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync($"UPDATE catalog.federation_settings SET {column} = '  '"));

        Assert.Equal((PostgresErrorCodes.CheckViolation, "ck_federation_settings_names_not_blank"), (error.SqlState, error.ConstraintName));
    }

    [Theory]
    [InlineData("UPDATE catalog.federation_settings SET sender_name = E'Uni\\nBcc'", "ck_federation_settings_sender_name")]
    [InlineData("UPDATE catalog.federation_settings SET sender_name = 'soporte@banco.example'", "ck_federation_settings_sender_name")]
    [InlineData("UPDATE catalog.federation_settings SET sender_name = 'Uni <x>'", "ck_federation_settings_sender_name")]
    [InlineData("UPDATE catalog.federation_settings SET website = 'http://federacion.example'", "ck_federation_settings_website")]
    public async Task Unsafe_sender_names_and_websites_are_refused(string sql, string constraint)
    {
        var error = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(sql));

        Assert.Equal((PostgresErrorCodes.CheckViolation, constraint), (error.SqlState, error.ConstraintName));
    }

    [Fact]
    public async Task The_version_changes_with_every_write()
    {
        var before = (await ReadAsync()).Version;

        await ExecuteAsync("UPDATE catalog.federation_settings SET short_name = 'Unión'");

        Assert.NotEqual(before, (await ReadAsync()).Version);
    }

    private async Task<FederationSettings> ReadAsync()
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FederationCatalogDbContext>();
        return await db.FederationSettings.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FederationCatalogDbContext>();
        await db.Database.ExecuteSqlRawAsync(sql, TestContext.Current.CancellationToken);
    }
}
