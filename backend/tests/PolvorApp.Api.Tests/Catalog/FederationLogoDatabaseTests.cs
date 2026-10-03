using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PolvorApp.Api.Platform.Database;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.FederationCatalog.Logos;
using PolvorApp.FederationCatalog.Persistence;

namespace PolvorApp.Api.Tests.Catalog;

/// <summary>
/// The Federation's settings (add-distribution-planning, design D11): one row, created by the
/// migration, whose logo columns follow the comparsa logo rules.
/// </summary>
public sealed class FederationLogoDatabaseTests(PostgresFixture postgres) : IAsyncLifetime
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
    public async Task The_migration_creates_the_single_settings_row_without_a_logo()
    {
        var settings = await ReadAsync();

        Assert.Equal(FederationSettings.SingletonId, settings.Id);
        Assert.Null(settings.Logo);
    }

    [Fact]
    public async Task A_second_settings_row_is_refused()
    {
        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteAsync("INSERT INTO catalog.federation_settings (id, updated_at) VALUES (2, now())"));

        Assert.Equal((PostgresErrorCodes.CheckViolation, "ck_federation_settings_single"), (error.SqlState, error.ConstraintName));
    }

    [Fact]
    public async Task A_logo_is_stored_under_the_row_lock_and_reads_back()
    {
        var logo = NewLogo();

        await ChangeLockedAsync(settings => settings.Logo = logo);

        var stored = (await ReadAsync()).Logo;
        Assert.Equal((logo.Id, logo.ObjectKey, logo.Width, logo.Height, logo.SizeBytes), (stored!.Id, stored.ObjectKey, stored.Width, stored.Height, stored.SizeBytes));
    }

    [Fact]
    public async Task A_partial_logo_is_refused()
    {
        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteAsync($"UPDATE catalog.federation_settings SET logo_id = '{Guid.CreateVersion7()}'"));

        Assert.Equal((PostgresErrorCodes.CheckViolation, "ck_federation_settings_logo_complete"), (error.SqlState, error.ConstraintName));
    }

    [Fact]
    public async Task A_key_outside_the_logo_prefix_is_refused()
    {
        var id = Guid.CreateVersion7();
        var error = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            $"UPDATE catalog.federation_settings SET logo_id = '{id}', logo_object_key = 'registry/photos/{id:N}.png', logo_width = 512, logo_height = 256, logo_size_bytes = 4096, logo_uploaded_at = now()"));

        Assert.Equal((PostgresErrorCodes.CheckViolation, "ck_federation_settings_logo_key"), (error.SqlState, error.ConstraintName));
    }

    private static ComparsaLogo NewLogo()
    {
        var id = Guid.CreateVersion7();
        return new ComparsaLogo { Id = id, ObjectKey = LogoStorage.KeyFor(id), Width = 512, Height = 256, SizeBytes = 4096, UploadedAt = DateTimeOffset.UtcNow };
    }

    private async Task ChangeLockedAsync(Action<FederationSettings> change)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FederationCatalogDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
        change(await db.LockFederationSettingsAsync(TestContext.Current.CancellationToken));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        await transaction.CommitAsync(TestContext.Current.CancellationToken);
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
