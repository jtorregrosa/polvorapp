using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using PolvorApp.ArquebusierRegistry.Arquebusiers;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ArquebusierRegistry.Lock;
using PolvorApp.ArquebusierRegistry.OwnedWeapons;
using PolvorApp.ArquebusierRegistry.Photos;
using PolvorApp.SharedKernel.Auditing;
using PolvorApp.SharedKernel.Codes;
using PolvorApp.SharedKernel.Persistence;

namespace PolvorApp.ArquebusierRegistry.Persistence;

/// <summary>
/// Schema <c>registry</c>: arquebusiers, their owned weapons (design D3) and the references to their
/// photos in the object storage (add-arquebusier-photos, design D4). The database
/// constraints back up the API's blocking rules against races. The foreign keys into the catalog
/// schema are added by the migration, because EF cannot model a key into another context.
/// </summary>
internal sealed class ArquebusierRegistryDbContext(DbContextOptions<ArquebusierRegistryDbContext> options) : DbContext(options)
{
    public const string Schema = "registry";

    /// <summary>Unique index names the services map to problem codes (BR-02, spec: Owned weapons).</summary>
    public const string NationalIdIndex = "ix_arquebusiers_national_id";
    public const string FederationIdIndex = "ix_arquebusiers_federation_id";
    public const string OwnershipGuideIndex = "ix_owned_weapons_ownership_guide_number";

    /// <summary>Cross-schema foreign keys added by the migration (design D3).</summary>
    public const string ComparsaForeignKey = "fk_arquebusiers_catalog_comparsas";
    public const string WeaponModelForeignKey = "fk_owned_weapons_catalog_weapon_models";

    /// <summary>EF-generated foreign key from an owned weapon to its arquebusier.</summary>
    public const string ArquebusierForeignKey = "fk_owned_weapons_arquebusiers_arquebusier_id";

    /// <summary>One photo of each kind per arquebusier: a concurrent first upload of the same kind loses on it.</summary>
    public const string PhotoKindIndex = "ix_arquebusier_photos_arquebusier_id_kind";

    /// <summary>EF-generated foreign key from a photo to its arquebusier.</summary>
    public const string PhotoArquebusierForeignKey = "fk_arquebusier_photos_arquebusiers_arquebusier_id";

    private const int CodeMaxLength = 16;

    public DbSet<Arquebusier> Arquebusiers => Set<Arquebusier>();

    public DbSet<OwnedWeapon> OwnedWeapons => Set<OwnedWeapon>();

    public DbSet<ArquebusierPhoto> Photos => Set<ArquebusierPhoto>();

    public DbSet<RegistrySettings> Settings => Set<RegistrySettings>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.AddAuditTrail();
        MapArquebusiers(modelBuilder);
        MapOwnedWeapons(modelBuilder);
        MapPhotos(modelBuilder);
        MapSettings(modelBuilder);
    }

    /// <summary>
    /// One row, inserted unlocked by the AddRegistryLock migration (add-festival-editions, design D8).
    /// Deliberately not <c>HasData</c>: its values change at run time, and a later migration would
    /// otherwise emit an <c>UpdateData</c> that silently unlocks a locked registry.
    /// </summary>
    private static void MapSettings(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<RegistrySettings>(settings =>
        {
            settings.ToTable("registry_settings", table =>
                table.HasCheckConstraint("ck_registry_settings_singleton", "id = " + RegistrySettings.SingletonId));
            settings.HasKey(s => s.Id);
            settings.Property(s => s.Id).ValueGeneratedNever();
        });

    private static void MapArquebusiers(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Arquebusier>(arquebusier =>
        {
            arquebusier.ToTable("arquebusiers", table =>
            {
                // Adding an enum member changes these lists: the model snapshot then asks for a migration.
                table.HasCheckConstraint("ck_arquebusiers_gender", In("gender", EnumCodes.All<Gender>()));
                table.HasCheckConstraint("ck_arquebusiers_status", In("status", EnumCodes.All<ArquebusierStatus>()));
                table.HasCheckConstraint("ck_arquebusiers_license_type", "license_type IS NULL OR " + In("license_type", EnumCodes.All<LicenseType>()));
                table.HasCheckConstraint("ck_arquebusiers_federation_id", "federation_id BETWEEN 1 AND 999999999");

                // BR-01 backstop: the normalised form only; the check letter is validated by the API.
                table.HasCheckConstraint("ck_arquebusiers_national_id", "national_id ~ '^([0-9]{8}|[XYZ][0-9]{7})[A-Z]$'");
                table.HasCheckConstraint("ck_arquebusiers_first_name", NameRule("first_name"));
                table.HasCheckConstraint("ck_arquebusiers_last_name", NameRule("last_name"));

                // "Not in the future" needs today's date, which a check constraint cannot use: the API enforces it.
                table.HasCheckConstraint("ck_arquebusiers_birth_date", "birth_date >= DATE '1900-01-01'");
                table.HasCheckConstraint("ck_arquebusiers_phone", "phone ~ '^[+]?[0-9 ]+$'");
                table.HasCheckConstraint("ck_arquebusiers_email", "email = lower(email) AND email = btrim(email) AND email <> ''");

                // Spec: Current license. No license, a pending one without dates, or an issued one
                // whose expiry is after its issue date.
                table.HasCheckConstraint(
                    "ck_arquebusiers_license",
                    "(license_type IS NULL AND NOT license_pending AND license_issued_on IS NULL AND license_expires_on IS NULL)"
                    + " OR (license_type IS NOT NULL AND license_pending AND license_issued_on IS NULL AND license_expires_on IS NULL)"
                    + " OR (license_type IS NOT NULL AND NOT license_pending AND license_issued_on IS NOT NULL"
                    + " AND license_expires_on IS NOT NULL AND license_expires_on > license_issued_on)");
            });
            arquebusier.HasKey(a => a.Id);
            arquebusier.Property(a => a.Id).ValueGeneratedNever();
            arquebusier.Property(a => a.NationalId).HasMaxLength(Arquebusier.NationalIdLength);
            arquebusier.Property(a => a.FirstName).HasMaxLength(Arquebusier.NameMaxLength);
            arquebusier.Property(a => a.LastName).HasMaxLength(Arquebusier.NameMaxLength);
            arquebusier.Property(a => a.Email).HasMaxLength(Arquebusier.EmailMaxLength);
            arquebusier.Property(a => a.Phone).HasMaxLength(Arquebusier.PhoneMaxLength);
            arquebusier.Property(a => a.Gender).HasConversion(new EnumCodeConverter<Gender>()).HasMaxLength(CodeMaxLength);
            arquebusier.Property(a => a.Status).HasConversion(new EnumCodeConverter<ArquebusierStatus>()).HasMaxLength(CodeMaxLength);
            arquebusier.Property(a => a.LicenseType).HasConversion(new EnumCodeConverter<LicenseType>()).HasMaxLength(CodeMaxLength);
            arquebusier.Property(a => a.Version).IsRowVersion();

            // Blocking (BR-02): unique across the whole Federation, whatever the comparsa.
            arquebusier.HasIndex(a => a.NationalId).IsUnique().HasDatabaseName(NationalIdIndex);
            arquebusier.HasIndex(a => a.FederationId).IsUnique().HasDatabaseName(FederationIdIndex);

            // Scoped lists (BR-12) and the catalog's usage check filter by comparsa.
            arquebusier.HasIndex(a => a.ComparsaId);
        });

    private static void MapOwnedWeapons(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<OwnedWeapon>(weapon =>
        {
            weapon.ToTable("owned_weapons", table =>
            {
                table.HasCheckConstraint("ck_owned_weapons_weapon_number", NotBlankTrimmed("weapon_number"));

                // Stored trimmed and upper-cased, so the unique index compares ignoring letter case.
                // COLLATE "C": upper() then folds ASCII only, whatever the database collation, so the
                // check never rejects a value the API upper-cased with the invariant culture.
                table.HasCheckConstraint(
                    "ck_owned_weapons_ownership_guide_number",
                    NotBlankTrimmed("ownership_guide_number")
                    + " AND ownership_guide_number COLLATE \"C\" = upper(ownership_guide_number COLLATE \"C\")");
            });
            weapon.HasKey(w => w.Id);
            weapon.Property(w => w.Id).ValueGeneratedNever();
            weapon.Property(w => w.WeaponNumber).HasMaxLength(OwnedWeapon.NumberMaxLength);
            weapon.Property(w => w.OwnershipGuideNumber).HasMaxLength(OwnedWeapon.NumberMaxLength);
            weapon.Property(w => w.Version).IsRowVersion();

            // Deleting an arquebusier erases their owned weapons (BR-14).
            weapon.HasOne<Arquebusier>().WithMany(a => a.OwnedWeapons).HasForeignKey(w => w.ArquebusierId).OnDelete(DeleteBehavior.Cascade);

            weapon.HasIndex(w => w.OwnershipGuideNumber).IsUnique().HasDatabaseName(OwnershipGuideIndex);

            // The catalog's usage check and the foreign key into catalog.weapon_models look up by model.
            weapon.HasIndex(w => w.WeaponModelId);
        });

    private static void MapPhotos(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<ArquebusierPhoto>(photo =>
        {
            photo.ToTable("arquebusier_photos", table =>
            {
                table.HasCheckConstraint("ck_arquebusier_photos_kind", In("kind", EnumCodes.All<ArquebusierPhotoKind>()));
                table.HasCheckConstraint("ck_arquebusier_photos_dimensions", "width > 0 AND height > 0 AND size_bytes > 0");
                // Under the registry's prefix, where the orphan sweep looks (design D2).
                table.HasCheckConstraint("ck_arquebusier_photos_object_key", "object_key LIKE 'registry/photos/%'");
            });
            photo.HasKey(p => p.Id);
            photo.Property(p => p.Id).ValueGeneratedNever();
            photo.Property(p => p.Kind).HasConversion(new EnumCodeConverter<ArquebusierPhotoKind>()).HasMaxLength(CodeMaxLength);
            photo.Property(p => p.ObjectKey).HasMaxLength(ArquebusierPhoto.ObjectKeyMaxLength);

            // Deleting an arquebusier removes the references; the objects are deleted after the commit (BR-14).
            photo.HasOne<Arquebusier>().WithMany().HasForeignKey(p => p.ArquebusierId).OnDelete(DeleteBehavior.Cascade);

            photo.HasIndex(p => new { p.ArquebusierId, p.Kind }).IsUnique().HasDatabaseName(PhotoKindIndex);

            // The orphan sweep asks which keys are referenced (design D2).
            photo.HasIndex(p => p.ObjectKey).IsUnique();
        });

    private static string NotBlankTrimmed(string column) =>
        "btrim(" + column + ") <> '' AND " + column + " = btrim(" + column + ")";

    /// <summary>Not blank, stored trimmed, and without control characters (line breaks included).</summary>
    private static string NameRule(string column) =>
        NotBlankTrimmed(column) + " AND " + column + " !~ '[[:cntrl:]]'";

    private static string Quote(string code) => "'" + code + "'";

    private static string In(string column, IEnumerable<string> codes) =>
        column + " IN (" + string.Join(", ", codes.Select(Quote)) + ")";
}

/// <summary>Lets <c>dotnet ef migrations add</c> build the model; it never connects.</summary>
internal sealed class ArquebusierRegistryDbContextDesignTimeFactory : IDesignTimeDbContextFactory<ArquebusierRegistryDbContext>
{
    public ArquebusierRegistryDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<ArquebusierRegistryDbContext>()
            .UseModuleDatabase("Host=design-time-only", ArquebusierRegistryDbContext.Schema)
            .Options);
}
