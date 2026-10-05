using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PolvorApp.Api.Platform.Database;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.FederationCatalog.Assignments;
using PolvorApp.FederationCatalog.Comparsas;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.FederationCatalog.Persistence;
using PolvorApp.FederationCatalog.WeaponModels;

namespace PolvorApp.Api.Tests.Catalog;

/// <summary>
/// Design D3: the database backs up every blocking catalogue rule, so a race past the API checks
/// still cannot store invalid data. Each case names the constraint the services map to a problem code.
/// </summary>
public sealed class CatalogDatabaseTests(PostgresFixture postgres) : IAsyncLifetime
{
    /// <summary>The catalogue migration before <c>AllowRentablePistols</c> dropped the pistol rule.</summary>
    private const string BeforeRentablePistols = "20261003171820_AddFederationLogo";

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

    [Theory]
    [InlineData("Comparsa Sintética Norte", "comparsa sintética NORTE")]
    [InlineData("COMPARSA ÉPICA ÑU", "comparsa épica ñu")]
    public async Task Comparsa_names_are_unique_ignoring_case_including_accented_capitals(string existing, string duplicate)
    {
        await SaveAsync(NewComparsa(existing));

        var error = await FailAsync(NewComparsa(duplicate));

        Assert.Equal((PostgresErrorCodes.UniqueViolation, FederationCatalogDbContext.ComparsaNameIndex), error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Blank_names_and_labels_are_rejected(string blank)
    {
        Assert.Equal((PostgresErrorCodes.CheckViolation, "ck_comparsas_name_not_blank"), await FailAsync(NewComparsa(blank)));
        Assert.Equal((PostgresErrorCodes.CheckViolation, "ck_weapon_models_label_not_blank"), await FailAsync(NewModel(WeaponKind.Pistol, blank, attributes: false)));
    }

    [Fact]
    public async Task Enums_are_stored_as_their_codes_and_unknown_codes_are_rejected()
    {
        var comparsa = NewComparsa("Comparsa Sintética Códigos");
        await SaveAsync(comparsa);

        Assert.Equal("CHRISTIAN", await ScalarAsync($"SELECT side FROM catalog.comparsas WHERE id = '{comparsa.Id}'"));
        var error = await Assert.ThrowsAsync<PostgresException>(() => ScalarAsync($"UPDATE catalog.comparsas SET side = 'NEUTRAL' WHERE id = '{comparsa.Id}' RETURNING side"));
        Assert.Equal((PostgresErrorCodes.CheckViolation, "ck_comparsas_side"), (error.SqlState, error.ConstraintName));
    }

    [Fact]
    public async Task A_rentable_arcabuz_and_a_pistol_with_partial_attributes_are_accepted()
    {
        var pistol = NewModel(WeaponKind.Pistol, "PISTOLA DIESTRA", attributes: false);
        pistol.Handedness = Handedness.Right;

        await SaveAsync(NewModel(WeaponKind.Arcabuz, "ARCABUZ PEQUEÑO", rentable: true), pistol);
    }

    [Fact]
    public async Task A_rentable_pistol_is_accepted()
    {
        var pistol = NewModel(WeaponKind.Pistol, "PISTOLA DE ALQUILER", rentable: true, attributes: false);

        await SaveAsync(pistol);

        Assert.True((bool?)await ScalarAsync($"SELECT rentable FROM catalog.weapon_models WHERE id = '{pistol.Id}'"));
    }

    [Fact]
    public async Task Rolling_back_the_pistol_rule_is_refused_while_a_rentable_pistol_exists()
    {
        await SaveAsync(NewModel(WeaponKind.Pistol, "PISTOLA DE ALQUILER", rentable: true, attributes: false));

        var error = await Assert.ThrowsAsync<PostgresException>(() => RollBackToAsync(BeforeRentablePistols));

        Assert.Contains("Rentable pistols exist", error.MessageText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rolling_back_the_pistol_rule_restores_its_constraint()
    {
        await RollBackToAsync(BeforeRentablePistols);

        var error = await FailAsync(NewModel(WeaponKind.Pistol, "PISTOLA", rentable: true, attributes: false));

        Assert.Equal((PostgresErrorCodes.CheckViolation, "ck_weapon_models_pistol_not_rentable"), error);
    }

    [Fact]
    public async Task An_arcabuz_without_handedness_is_rejected()
    {
        var model = NewModel(WeaponKind.Arcabuz, "ARCABUZ SIN MANO");
        model.Handedness = null;

        var error = await FailAsync(model);

        Assert.Equal((PostgresErrorCodes.CheckViolation, "ck_weapon_models_attributes"), error);
    }

    [Fact]
    public async Task A_duplicate_combination_is_rejected_but_pistols_without_attributes_can_repeat()
    {
        await SaveAsync(NewModel(WeaponKind.Arcabuz, "ARCABUZ MORO DIESTRO"));
        await SaveAsync(NewModel(WeaponKind.Pistol, "PISTOLA", attributes: false));
        await SaveAsync(NewModel(WeaponKind.Pistol, "PISTOLA ANTIGUA", attributes: false));

        var error = await FailAsync(NewModel(WeaponKind.Arcabuz, "ARCABUZ MORO DIESTRO BIS"));

        Assert.Equal((PostgresErrorCodes.UniqueViolation, FederationCatalogDbContext.WeaponModelCombinationIndex), error);
    }

    [Fact]
    public async Task Labels_are_unique_ignoring_case_including_accented_capitals()
    {
        await SaveAsync(NewModel(WeaponKind.Pistol, "Pistola Pequeña", attributes: false));

        var error = await FailAsync(NewModel(WeaponKind.Pistol, "PISTOLA PEQUEÑA", attributes: false));

        Assert.Equal((PostgresErrorCodes.UniqueViolation, FederationCatalogDbContext.WeaponModelLabelIndex), error);
    }

    [Fact]
    public async Task An_assignment_cannot_exist_twice()
    {
        var comparsa = NewComparsa("Comparsa Sintética Sur");
        var userId = Guid.CreateVersion7();
        await SaveAsync(comparsa, NewAssignment(comparsa.Id, userId));

        var error = await FailAsync(NewAssignment(comparsa.Id, userId));

        Assert.Equal((PostgresErrorCodes.UniqueViolation, "pk_firing_chief_assignments"), error);
    }

    [Fact]
    public async Task An_assignment_needs_an_existing_comparsa()
    {
        var error = await FailAsync(NewAssignment(Guid.CreateVersion7(), Guid.CreateVersion7()));

        Assert.Equal((PostgresErrorCodes.ForeignKeyViolation, "fk_firing_chief_assignments_comparsas_comparsa_id"), error);
    }

    [Fact]
    public async Task Removing_a_tracked_comparsa_deletes_its_assignments()
    {
        var comparsa = NewComparsa("Comparsa Sintética Rastreada");
        var userId = Guid.CreateVersion7();
        await SaveAsync(comparsa, NewAssignment(comparsa.Id, userId));

        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FederationCatalogDbContext>();
        db.Comparsas.Remove(await db.Comparsas.SingleAsync(c => c.Id == comparsa.Id, TestContext.Current.CancellationToken));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.False(await db.Assignments.AnyAsync(a => a.UserId == userId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Deleting_a_comparsa_deletes_its_assignments()
    {
        var comparsa = NewComparsa("Comparsa Sintética Este");
        var other = NewComparsa("Comparsa Sintética Oeste");
        var userId = Guid.CreateVersion7();
        await SaveAsync(comparsa, other, NewAssignment(comparsa.Id, userId), NewAssignment(other.Id, userId));

        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FederationCatalogDbContext>();
        await db.Comparsas.Where(c => c.Id == comparsa.Id).ExecuteDeleteAsync(TestContext.Current.CancellationToken);

        Assert.Equal([other.Id], await db.Assignments.Where(a => a.UserId == userId).Select(a => a.ComparsaId).ToListAsync(TestContext.Current.CancellationToken));
    }

    private static Comparsa NewComparsa(string name) =>
        new() { Id = Guid.CreateVersion7(), Name = name, Side = Side.Christian, CreatedAt = DateTimeOffset.UtcNow };

    private static FiringChiefAssignment NewAssignment(Guid comparsaId, Guid userId) =>
        new() { ComparsaId = comparsaId, UserId = userId, AssignedAt = DateTimeOffset.UtcNow };

    private static WeaponModel NewModel(WeaponKind kind, string label, bool rentable = false, bool attributes = true) => new()
    {
        Id = Guid.CreateVersion7(),
        Kind = kind,
        Label = label,
        Rentable = rentable,
        Side = attributes ? Side.Moorish : null,
        Handedness = attributes ? Handedness.Right : null,
        Size = attributes ? WeaponSize.Normal : null,
        CreatedAt = DateTimeOffset.UtcNow,
    };

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

    private async Task RollBackToAsync(string migration)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FederationCatalogDbContext>();
        await db.GetService<IMigrator>().MigrateAsync(migration, TestContext.Current.CancellationToken);
    }

    private async Task<object?> ScalarAsync(string sql)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var connection = scope.ServiceProvider.GetRequiredService<FederationCatalogDbContext>().Database.GetDbConnection();
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
    }
}
