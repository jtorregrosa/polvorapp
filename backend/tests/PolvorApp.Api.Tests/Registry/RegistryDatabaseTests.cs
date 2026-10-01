using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PolvorApp.Api.Platform.Database;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ArquebusierRegistry.Persistence;
using PolvorApp.FederationCatalog.Comparsas;
using PolvorApp.FederationCatalog.WeaponModels;

namespace PolvorApp.Api.Tests.Registry;

/// <summary>
/// Design D3: the database backs up every blocking registry rule, so a race past the API checks
/// still cannot store invalid data. Each case names the constraint the services map to a problem code.
/// </summary>
[Collection(PostgresGroup.Name)]
public sealed class RegistryDatabaseTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly Comparsa _comparsa = RegistryData.NewComparsa("Comparsa Sintética Registro");
    private readonly WeaponModel _model = RegistryData.NewWeaponModel("ARCABUZ SINTÉTICO");
    private ApiFactory? _factory;
    private string _connectionString = string.Empty;

    private IServiceProvider Services => _factory!.Services;

    public async ValueTask InitializeAsync()
    {
        _connectionString = await postgres.CreateDatabaseAsync();
        _factory = new ApiFactory(_connectionString);
        Assert.Equal(0, await MigrateCommand.RunAsync(Services, TestContext.Current.CancellationToken));
        await Services.SaveCatalogAsync(_comparsa, _model);
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
    }

    [Fact]
    public async Task The_national_id_is_unique()
    {
        var first = RegistryData.NewArquebusier(_comparsa.Id);
        await Services.SaveRegistryAsync(first);
        var second = RegistryData.NewArquebusier(_comparsa.Id);
        second.NationalId = first.NationalId;

        Assert.Equal((PostgresErrorCodes.UniqueViolation, ArquebusierRegistryDbContext.NationalIdIndex), await FailAsync(second));
    }

    [Fact]
    public async Task The_federation_id_is_unique()
    {
        var first = RegistryData.NewArquebusier(_comparsa.Id);
        await Services.SaveRegistryAsync(first);
        var second = RegistryData.NewArquebusier(_comparsa.Id);
        second.FederationId = first.FederationId;

        Assert.Equal((PostgresErrorCodes.UniqueViolation, ArquebusierRegistryDbContext.FederationIdIndex), await FailAsync(second));
    }

    [Fact]
    public async Task The_ownership_guide_is_unique_and_stored_upper_cased()
    {
        var owner = RegistryData.NewArquebusier(_comparsa.Id);
        var other = RegistryData.NewArquebusier(_comparsa.Id);
        await Services.SaveRegistryAsync(owner, other, RegistryData.NewOwnedWeapon(owner.Id, _model.Id, "SINT-0001"));

        Assert.Equal(
            (PostgresErrorCodes.UniqueViolation, ArquebusierRegistryDbContext.OwnershipGuideIndex),
            await FailAsync(RegistryData.NewOwnedWeapon(other.Id, _model.Id, "SINT-0001")));
        Assert.Equal(
            (PostgresErrorCodes.CheckViolation, "ck_owned_weapons_ownership_guide_number"),
            await FailAsync(RegistryData.NewOwnedWeapon(other.Id, _model.Id, "sint-0002")));
    }

    [Theory]
    [InlineData("pending with dates")]
    [InlineData("expiry not after issue")]
    [InlineData("dates without type")]
    [InlineData("issued without expiry")]
    [InlineData("pending without type")]
    public async Task An_inconsistent_license_is_rejected(string case_)
    {
        var arquebusier = RegistryData.NewArquebusier(_comparsa.Id);
        var issued = new DateOnly(2024, 3, 10);
        switch (case_)
        {
            case "pending with dates":
                (arquebusier.LicenseType, arquebusier.LicensePending, arquebusier.LicenseIssuedOn, arquebusier.LicenseExpiresOn) = (LicenseType.Ae, true, issued, issued.AddYears(5));
                break;
            case "expiry not after issue":
                (arquebusier.LicenseType, arquebusier.LicenseIssuedOn, arquebusier.LicenseExpiresOn) = (LicenseType.Ae, issued, issued);
                break;
            case "dates without type":
                (arquebusier.LicenseIssuedOn, arquebusier.LicenseExpiresOn) = (issued, issued.AddYears(5));
                break;
            case "issued without expiry":
                (arquebusier.LicenseType, arquebusier.LicenseIssuedOn) = (LicenseType.AProf, issued);
                break;
            default:
                arquebusier.LicensePending = true;
                break;
        }

        Assert.Equal((PostgresErrorCodes.CheckViolation, "ck_arquebusiers_license"), await FailAsync(arquebusier));
    }

    [Fact]
    public async Task Valid_licenses_are_accepted()
    {
        var none = RegistryData.NewArquebusier(_comparsa.Id);
        var pending = RegistryData.NewArquebusier(_comparsa.Id);
        (pending.LicenseType, pending.LicensePending) = (LicenseType.Ae, true);
        var issued = RegistryData.NewArquebusier(_comparsa.Id);
        (issued.LicenseType, issued.LicenseIssuedOn, issued.LicenseExpiresOn) = (LicenseType.AProf, new DateOnly(2026, 2, 1), new DateOnly(2027, 2, 1));

        await Services.SaveRegistryAsync(none, pending, issued);
    }

    [Theory]
    [InlineData("federation id too large", "ck_arquebusiers_federation_id")]
    [InlineData("birth date before 1900", "ck_arquebusiers_birth_date")]
    [InlineData("phone with letters", "ck_arquebusiers_phone")]
    [InlineData("email in upper case", "ck_arquebusiers_email")]
    [InlineData("empty email", "ck_arquebusiers_email")]
    [InlineData("untrimmed first name", "ck_arquebusiers_first_name")]
    [InlineData("line break in last name", "ck_arquebusiers_last_name")]
    public async Task Field_rules_are_backed_up_by_the_database(string case_, string constraint)
    {
        var arquebusier = RegistryData.NewArquebusier(_comparsa.Id);
        switch (case_)
        {
            case "federation id too large": arquebusier.FederationId = 1_000_000_000; break;
            case "birth date before 1900": arquebusier.BirthDate = new DateOnly(1899, 12, 31); break;
            case "phone with letters": arquebusier.Phone = "+34 600 ABC"; break;
            case "email in upper case": arquebusier.Email = "Sintetica@polvorapp.example"; break;
            case "empty email": arquebusier.Email = string.Empty; break;
            case "untrimmed first name": arquebusier.FirstName = " Arcabucero"; break;
            default: arquebusier.LastName = "Sintético" + (char)10 + "Prueba"; break;
        }

        Assert.Equal((PostgresErrorCodes.CheckViolation, constraint), await FailAsync(arquebusier));
    }

    [Fact]
    public async Task An_untrimmed_ownership_guide_or_weapon_number_is_rejected()
    {
        var owner = RegistryData.NewArquebusier(_comparsa.Id);
        await Services.SaveRegistryAsync(owner);
        var untrimmedNumber = RegistryData.NewOwnedWeapon(owner.Id, _model.Id, "SINT-0400");
        untrimmedNumber.WeaponNumber = "1234 ";

        Assert.Equal((PostgresErrorCodes.CheckViolation, "ck_owned_weapons_ownership_guide_number"), await FailAsync(RegistryData.NewOwnedWeapon(owner.Id, _model.Id, " SINT-0401")));
        Assert.Equal((PostgresErrorCodes.CheckViolation, "ck_owned_weapons_weapon_number"), await FailAsync(untrimmedNumber));
    }

    [Fact]
    public async Task Valid_contact_data_and_an_upper_cased_accented_guide_are_accepted()
    {
        var arquebusier = RegistryData.NewArquebusier(_comparsa.Id);
        (arquebusier.Email, arquebusier.Phone) = ("sintetica@polvorapp.example", "+34 600 000 001");
        await Services.SaveRegistryAsync(arquebusier, RegistryData.NewOwnedWeapon(arquebusier.Id, _model.Id, "AÑ-123-45"));
    }

    [Fact]
    public async Task A_national_id_outside_the_normalised_form_is_rejected()
    {
        var arquebusier = RegistryData.NewArquebusier(_comparsa.Id);
        arquebusier.NationalId = "1234567z";

        Assert.Equal((PostgresErrorCodes.CheckViolation, "ck_arquebusiers_national_id"), await FailAsync(arquebusier));
    }

    [Fact]
    public async Task An_unknown_comparsa_or_weapon_model_is_rejected()
    {
        Assert.Equal(
            (PostgresErrorCodes.ForeignKeyViolation, ArquebusierRegistryDbContext.ComparsaForeignKey),
            await FailAsync(RegistryData.NewArquebusier(Guid.CreateVersion7())));

        var owner = RegistryData.NewArquebusier(_comparsa.Id);
        await Services.SaveRegistryAsync(owner);
        Assert.Equal(
            (PostgresErrorCodes.ForeignKeyViolation, ArquebusierRegistryDbContext.WeaponModelForeignKey),
            await FailAsync(RegistryData.NewOwnedWeapon(owner.Id, Guid.CreateVersion7(), "SINT-0100")));
    }

    [Fact]
    public async Task Deleting_an_arquebusier_deletes_their_owned_weapons()
    {
        var owner = RegistryData.NewArquebusier(_comparsa.Id);
        await Services.SaveRegistryAsync(owner, RegistryData.NewOwnedWeapon(owner.Id, _model.Id, "SINT-0200"), RegistryData.NewOwnedWeapon(owner.Id, _model.Id, "SINT-0201"));

        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ArquebusierRegistryDbContext>();
        await db.Arquebusiers.Where(a => a.Id == owner.Id).ExecuteDeleteAsync(TestContext.Current.CancellationToken);

        Assert.False(await db.OwnedWeapons.AnyAsync(w => w.ArquebusierId == owner.Id, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("fk_arquebusiers_catalog_comparsas", "registry.arquebusiers", "catalog.comparsas")]
    [InlineData("fk_owned_weapons_catalog_weapon_models", "registry.owned_weapons", "catalog.weapon_models")]
    public async Task The_cross_schema_foreign_keys_exist_with_no_action(string constraint, string table, string referenced)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT conrelid::regclass::text, confrelid::regclass::text, confdeltype::text FROM pg_constraint WHERE conname = @name AND contype = 'f'",
            connection);
        command.Parameters.AddWithValue("name", constraint);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);

        Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken), constraint);
        Assert.Equal((table, referenced, "a"), (reader.GetString(0), reader.GetString(1), reader.GetString(2)));
    }

    [Fact]
    public async Task A_referenced_comparsa_or_weapon_model_cannot_be_deleted_in_the_database()
    {
        var owner = RegistryData.NewArquebusier(_comparsa.Id);
        await Services.SaveRegistryAsync(owner, RegistryData.NewOwnedWeapon(owner.Id, _model.Id, "SINT-0500"));

        Assert.Equal(
            (PostgresErrorCodes.ForeignKeyViolation, ArquebusierRegistryDbContext.ComparsaForeignKey),
            await FailRawAsync($"DELETE FROM catalog.comparsas WHERE id = '{_comparsa.Id}'"));
        Assert.Equal(
            (PostgresErrorCodes.ForeignKeyViolation, ArquebusierRegistryDbContext.WeaponModelForeignKey),
            await FailRawAsync($"DELETE FROM catalog.weapon_models WHERE id = '{_model.Id}'"));
    }

    [Theory]
    [InlineData("federation id zero", "ck_arquebusiers_federation_id")]
    [InlineData("blank first name", "ck_arquebusiers_first_name")]
    [InlineData("blank last name", "ck_arquebusiers_last_name")]
    [InlineData("unknown gender", "ck_arquebusiers_gender")]
    [InlineData("unknown status", "ck_arquebusiers_status")]
    [InlineData("unknown license type", "ck_arquebusiers_license_type")]
    public async Task Raw_values_outside_the_rules_are_rejected(string case_, string constraint)
    {
        var arquebusier = RegistryData.NewArquebusier(_comparsa.Id);
        await Services.SaveRegistryAsync(arquebusier);
        var set = case_ switch
        {
            "federation id zero" => "federation_id = 0",
            "blank first name" => "first_name = '  '",
            "blank last name" => "last_name = ''",
            "unknown gender" => "gender = 'OTHER'",
            "unknown status" => "status = 'INACTIVE'",
            _ => "license_type = 'B', license_pending = true",
        };

        Assert.Equal((PostgresErrorCodes.CheckViolation, constraint), await FailRawAsync($"UPDATE registry.arquebusiers SET {set} WHERE id = '{arquebusier.Id}'"));
    }

    [Fact]
    public async Task Blank_weapon_numbers_and_guides_are_rejected()
    {
        var owner = RegistryData.NewArquebusier(_comparsa.Id);
        await Services.SaveRegistryAsync(owner);
        var blankNumber = RegistryData.NewOwnedWeapon(owner.Id, _model.Id, "SINT-0600");
        blankNumber.WeaponNumber = "  ";

        Assert.Equal((PostgresErrorCodes.CheckViolation, "ck_owned_weapons_weapon_number"), await FailAsync(blankNumber));
        Assert.Equal((PostgresErrorCodes.CheckViolation, "ck_owned_weapons_ownership_guide_number"), await FailAsync(RegistryData.NewOwnedWeapon(owner.Id, _model.Id, "")));
    }

    [Fact]
    public async Task A_stale_version_is_rejected_and_a_new_weapon_leaves_the_parent_version_unchanged()
    {
        var arquebusier = RegistryData.NewArquebusier(_comparsa.Id);
        var weapon = RegistryData.NewOwnedWeapon(arquebusier.Id, _model.Id, "SINT-0700");
        await Services.SaveRegistryAsync(arquebusier, weapon);
        var ct = TestContext.Current.CancellationToken;

        await using var first = Services.CreateAsyncScope();
        await using var second = Services.CreateAsyncScope();
        var firstDb = first.ServiceProvider.GetRequiredService<ArquebusierRegistryDbContext>();
        var secondDb = second.ServiceProvider.GetRequiredService<ArquebusierRegistryDbContext>();
        var firstCopy = await firstDb.Arquebusiers.SingleAsync(a => a.Id == arquebusier.Id, ct);
        var secondCopy = await secondDb.Arquebusiers.SingleAsync(a => a.Id == arquebusier.Id, ct);
        var firstWeapon = await firstDb.OwnedWeapons.SingleAsync(w => w.Id == weapon.Id, ct);
        var secondWeapon = await secondDb.OwnedWeapons.SingleAsync(w => w.Id == weapon.Id, ct);

        // Adding a weapon locks the parent FOR KEY SHARE but does not update it.
        var versionBefore = firstCopy.Version;
        await Services.SaveRegistryAsync(RegistryData.NewOwnedWeapon(arquebusier.Id, _model.Id, "SINT-0701"));
        await firstDb.Entry(firstCopy).ReloadAsync(ct);
        Assert.Equal(versionBefore, firstCopy.Version);

        firstCopy.Phone = "600000001";
        firstWeapon.WeaponNumber = "1";
        await firstDb.SaveChangesAsync(ct);
        secondCopy.Phone = "600000002";

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => secondDb.SaveChangesAsync(ct));
        secondDb.ChangeTracker.Clear();
        secondDb.Attach(secondWeapon);
        secondWeapon.WeaponNumber = "2";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => secondDb.SaveChangesAsync(ct));
    }

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
