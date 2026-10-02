using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PolvorApp.Api.Platform.Database;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.FederationCatalog.Comparsas;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.FederationCatalog.Logos;
using PolvorApp.FederationCatalog.Persistence;

namespace PolvorApp.Api.Tests.Catalog;

/// <summary>
/// Design D1: a comparsa's logo lives in nullable columns of its row, all set or all null, and the
/// database refuses a logo outside the catalogue's storage prefix or one stored twice.
/// </summary>
public sealed class LogoDatabaseTests(PostgresFixture postgres) : IAsyncLifetime
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
    public async Task A_comparsa_without_a_logo_reads_back_without_one()
    {
        var comparsa = NewComparsa("Comparsa Sintética Sin Logo");
        await SaveAsync(comparsa);

        Assert.Null((await ReadAsync(comparsa.Id)).Logo);
    }

    [Fact]
    public async Task A_logo_reads_back_with_every_field()
    {
        var logo = NewLogo();
        var comparsa = NewComparsa("Comparsa Sintética Con Logo", logo);
        await SaveAsync(comparsa);

        var stored = (await ReadAsync(comparsa.Id)).Logo;

        Assert.NotNull(stored);
        Assert.Equal(
            (logo.Id, logo.ObjectKey, logo.Width, logo.Height, logo.SizeBytes, logo.UploadedAt),
            (stored.Id, stored.ObjectKey, stored.Width, stored.Height, stored.SizeBytes, stored.UploadedAt));
    }

    [Fact]
    public async Task A_partial_logo_is_rejected()
    {
        var comparsa = NewComparsa("Comparsa Sintética Parcial");
        await SaveAsync(comparsa);
        var logoId = Guid.CreateVersion7();

        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FederationCatalogDbContext>();
        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlAsync($"UPDATE catalog.comparsas SET logo_id = {logoId} WHERE id = {comparsa.Id}", TestContext.Current.CancellationToken));

        Assert.Equal((PostgresErrorCodes.CheckViolation, "ck_comparsas_logo_complete"), (error.SqlState, error.ConstraintName));
    }

    [Theory]
    [InlineData("registry/photos/{0}.png")]
    [InlineData("catalog/logos/{0}.jpg")]
    [InlineData("catalog/logos/other.png")]
    [InlineData("catalog/logos/")]
    public async Task A_key_that_is_not_derived_from_the_logo_id_is_rejected(string keyFormat)
    {
        var logo = NewLogo();
        var wrong = With(logo, objectKey: string.Format(System.Globalization.CultureInfo.InvariantCulture, keyFormat, logo.Id));

        var error = await FailAsync(NewComparsa("Comparsa Sintética Fuera", wrong));

        Assert.Equal((PostgresErrorCodes.CheckViolation, "ck_comparsas_logo_key"), error);
    }

    [Theory]
    [InlineData(0, 256, 4096)]
    [InlineData(512, -1, 4096)]
    [InlineData(512, 256, 0)]
    public async Task Non_positive_dimensions_or_sizes_are_rejected(int width, int height, int sizeBytes)
    {
        var wrong = With(NewLogo(), width: width, height: height, sizeBytes: sizeBytes);

        var error = await FailAsync(NewComparsa("Comparsa Sintética Vacía", wrong));

        Assert.Equal((PostgresErrorCodes.CheckViolation, "ck_comparsas_logo_size"), error);
    }

    [Fact]
    public async Task A_first_logo_is_added_to_a_locked_comparsa()
    {
        var comparsa = NewComparsa("Comparsa Sintética Primera Vez");
        await SaveAsync(comparsa);
        var logo = NewLogo();

        await ChangeLockedAsync(comparsa.Id, locked =>
        {
            Assert.Null(locked.Logo);
            locked.Logo = logo;
        });

        Assert.Equal(logo.ObjectKey, (await ReadAsync(comparsa.Id)).Logo?.ObjectKey);
    }

    [Fact]
    public async Task The_row_lock_loads_the_logo_and_removing_it_clears_every_column()
    {
        var comparsa = NewComparsa("Comparsa Sintética Sin Logo Ya", NewLogo());
        await SaveAsync(comparsa);

        await ChangeLockedAsync(comparsa.Id, locked =>
        {
            Assert.NotNull(locked.Logo);
            locked.Logo = null;
        });

        Assert.Null((await ReadAsync(comparsa.Id)).Logo);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FederationCatalogDbContext>();
        var withAnyColumn = await db.Database.SqlQuery<int>($"""
            SELECT count(*)::int AS "Value" FROM catalog.comparsas WHERE id = {comparsa.Id}
            AND (logo_id IS NOT NULL OR logo_object_key IS NOT NULL OR logo_width IS NOT NULL
                 OR logo_height IS NOT NULL OR logo_size_bytes IS NOT NULL OR logo_uploaded_at IS NOT NULL)
            """).SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, withAnyColumn);
    }

    [Fact]
    public async Task A_name_edit_and_a_logo_upload_loaded_together_keep_both_changes()
    {
        var comparsa = NewComparsa("Comparsa Sintética Antes");
        await SaveAsync(comparsa);
        var logo = NewLogo();

        // Both loaded before either saves (design D1): EF writes only the columns each one changed.
        await using var editScope = _factory!.Services.CreateAsyncScope();
        await using var logoScope = _factory.Services.CreateAsyncScope();
        var editDb = editScope.ServiceProvider.GetRequiredService<FederationCatalogDbContext>();
        var logoDb = logoScope.ServiceProvider.GetRequiredService<FederationCatalogDbContext>();
        var edited = await editDb.Comparsas.SingleAsync(c => c.Id == comparsa.Id, TestContext.Current.CancellationToken);
        var withLogo = await logoDb.Comparsas.SingleAsync(c => c.Id == comparsa.Id, TestContext.Current.CancellationToken);
        edited.Name = "Comparsa Sintética Después";
        withLogo.Logo = logo;
        await logoDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        await editDb.SaveChangesAsync(TestContext.Current.CancellationToken);

        var stored = await ReadAsync(comparsa.Id);
        Assert.Equal(("Comparsa Sintética Después", logo.ObjectKey), (stored.Name, stored.Logo?.ObjectKey));
    }

    [Fact]
    public async Task One_stored_logo_belongs_to_one_comparsa()
    {
        var logo = NewLogo();
        await SaveAsync(NewComparsa("Comparsa Sintética Primera", logo));

        var error = await FailAsync(NewComparsa("Comparsa Sintética Segunda", With(logo)));

        Assert.Equal((PostgresErrorCodes.UniqueViolation, FederationCatalogDbContext.ComparsaLogoKeyIndex), error);
    }

    [Fact]
    public async Task Replacing_the_logo_rewrites_the_same_row()
    {
        var comparsa = NewComparsa("Comparsa Sintética Cambiante", NewLogo());
        await SaveAsync(comparsa);
        var replacement = NewLogo();

        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FederationCatalogDbContext>();
            var tracked = await db.Comparsas.SingleAsync(c => c.Id == comparsa.Id, TestContext.Current.CancellationToken);
            tracked.Logo = replacement;
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        Assert.Equal(replacement.ObjectKey, (await ReadAsync(comparsa.Id)).Logo?.ObjectKey);
    }

    private static Comparsa NewComparsa(string name, ComparsaLogo? logo = null) =>
        new() { Id = Guid.CreateVersion7(), Name = name, Side = Side.Moorish, CreatedAt = DateTimeOffset.UtcNow, Logo = logo };

    private static ComparsaLogo NewLogo()
    {
        var id = Guid.CreateVersion7();
        return new ComparsaLogo
        {
            Id = id,
            ObjectKey = LogoStorage.KeyFor(id),
            Width = 512,
            Height = 256,
            SizeBytes = 4096,
            UploadedAt = new DateTimeOffset(2026, 10, 2, 10, 0, 0, TimeSpan.Zero),
        };
    }

    /// <summary>A copy of <paramref name="logo"/>, with the given fields replaced.</summary>
    private static ComparsaLogo With(ComparsaLogo logo, string? objectKey = null, int? width = null, int? height = null, int? sizeBytes = null) => new()
    {
        Id = logo.Id,
        ObjectKey = objectKey ?? logo.ObjectKey,
        Width = width ?? logo.Width,
        Height = height ?? logo.Height,
        SizeBytes = sizeBytes ?? logo.SizeBytes,
        UploadedAt = logo.UploadedAt,
    };

    /// <summary>Locks the comparsa as the services do, applies <paramref name="change"/> and commits.</summary>
    private async Task ChangeLockedAsync(Guid id, Action<Comparsa> change)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FederationCatalogDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
        var locked = await db.LockComparsaForChangeAsync(id, TestContext.Current.CancellationToken);
        change(Assert.IsType<Comparsa>(locked));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        await transaction.CommitAsync(TestContext.Current.CancellationToken);
    }

    private async Task<Comparsa> ReadAsync(Guid id)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FederationCatalogDbContext>();
        return await db.Comparsas.AsNoTracking().SingleAsync(c => c.Id == id, TestContext.Current.CancellationToken);
    }

    private async Task SaveAsync(params object[] entities)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FederationCatalogDbContext>();
        db.AddRange(entities);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<(string SqlState, string? Constraint)> FailAsync(object entity)
    {
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => SaveAsync(entity));
        var database = Assert.IsType<PostgresException>(error.InnerException);
        return (database.SqlState, database.ConstraintName);
    }
}
