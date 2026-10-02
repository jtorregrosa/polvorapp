using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PolvorApp.Api.Platform.Database;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ArquebusierRegistry.Persistence;
using PolvorApp.ArquebusierRegistry.Photos;
using PolvorApp.FederationCatalog.Comparsas;

namespace PolvorApp.Api.Tests.Registry;

/// <summary>Design D4 (add-arquebusier-photos): the database backs up the photo rules.</summary>
public sealed class PhotoDatabaseTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly Comparsa _comparsa = RegistryData.NewComparsa("Comparsa Sintética Fotos");
    private ApiFactory? _factory;
    private string _connectionString = string.Empty;

    private IServiceProvider Services => _factory!.Services;

    public async ValueTask InitializeAsync()
    {
        _connectionString = await postgres.CreateMigratedDatabaseAsync();
        _factory = new ApiFactory(_connectionString);
        Assert.Equal(0, await MigrateCommand.RunAsync(Services, TestContext.Current.CancellationToken));
        await Services.SaveCatalogAsync(_comparsa);
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
    }

    [Fact]
    public async Task An_arquebusier_has_at_most_one_photo_of_each_kind()
    {
        var arquebusier = RegistryData.NewArquebusier(_comparsa.Id);
        await Services.SaveRegistryAsync(
            arquebusier,
            RegistryData.NewPhoto(arquebusier.Id, ArquebusierPhotoKind.Id),
            RegistryData.NewPhoto(arquebusier.Id, ArquebusierPhotoKind.LicenseFront),
            RegistryData.NewPhoto(arquebusier.Id, ArquebusierPhotoKind.LicenseBack));

        Assert.Equal(
            (PostgresErrorCodes.UniqueViolation, ArquebusierRegistryDbContext.PhotoKindIndex),
            await FailAsync(RegistryData.NewPhoto(arquebusier.Id, ArquebusierPhotoKind.Id)));
    }

    [Fact]
    public async Task An_object_key_belongs_to_one_photo()
    {
        var first = RegistryData.NewArquebusier(_comparsa.Id);
        var second = RegistryData.NewArquebusier(_comparsa.Id);
        var photo = RegistryData.NewPhoto(first.Id, ArquebusierPhotoKind.Id);
        await Services.SaveRegistryAsync(first, second, photo);
        var copy = new ArquebusierPhoto
        {
            Id = Guid.CreateVersion7(),
            ArquebusierId = second.Id,
            Kind = ArquebusierPhotoKind.Id,
            ObjectKey = photo.ObjectKey,
            Width = 600,
            Height = 800,
            SizeBytes = 1000,
            UploadedAt = DateTimeOffset.UtcNow,
        };

        Assert.Equal((PostgresErrorCodes.UniqueViolation, "ix_arquebusier_photos_object_key"), await FailAsync(copy));
    }

    [Theory]
    [InlineData("kind = 'PASSPORT'", "ck_arquebusier_photos_kind")]
    [InlineData("width = 0", "ck_arquebusier_photos_dimensions")]
    [InlineData("height = -1", "ck_arquebusier_photos_dimensions")]
    [InlineData("size_bytes = 0", "ck_arquebusier_photos_dimensions")]
    [InlineData("object_key = ' '", "ck_arquebusier_photos_object_key")]
    [InlineData("object_key = 'catalog/logos/x.png'", "ck_arquebusier_photos_object_key")]
    public async Task Raw_values_outside_the_rules_are_rejected(string set, string constraint)
    {
        var arquebusier = RegistryData.NewArquebusier(_comparsa.Id);
        var photo = RegistryData.NewPhoto(arquebusier.Id, ArquebusierPhotoKind.Id);
        await Services.SaveRegistryAsync(arquebusier, photo);

        Assert.Equal(
            (PostgresErrorCodes.CheckViolation, constraint),
            await FailRawAsync($"UPDATE registry.arquebusier_photos SET {set} WHERE id = '{photo.Id}'"));
    }

    [Fact]
    public async Task Deleting_an_arquebusier_deletes_their_photo_references()
    {
        var arquebusier = RegistryData.NewArquebusier(_comparsa.Id);
        await Services.SaveRegistryAsync(arquebusier, RegistryData.NewPhoto(arquebusier.Id, ArquebusierPhotoKind.Id));

        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ArquebusierRegistryDbContext>();
        await db.Arquebusiers.Where(a => a.Id == arquebusier.Id).ExecuteDeleteAsync(TestContext.Current.CancellationToken);

        Assert.False(await db.Photos.AnyAsync(p => p.ArquebusierId == arquebusier.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_photo_needs_an_existing_arquebusier() =>
        Assert.Equal(
            (PostgresErrorCodes.ForeignKeyViolation, ArquebusierRegistryDbContext.PhotoArquebusierForeignKey),
            await FailAsync(RegistryData.NewPhoto(Guid.CreateVersion7(), ArquebusierPhotoKind.Id)));

    private async Task<(string SqlState, string? Constraint)> FailAsync(object entity)
    {
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => Services.SaveRegistryAsync(entity));
        var database = Assert.IsType<PostgresException>(error.InnerException);
        return (database.SqlState, database.ConstraintName);
    }

    private async Task<(string SqlState, string? Constraint)> FailRawAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
        return (error.SqlState, error.ConstraintName);
    }
}
