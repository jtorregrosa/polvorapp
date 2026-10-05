using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PolvorApp.FederationCatalog.Assignments;
using PolvorApp.FederationCatalog.Comparsas;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.FederationCatalog.Logos;
using PolvorApp.FederationCatalog.WeaponModels;
using PolvorApp.SharedKernel.Auditing;
using PolvorApp.SharedKernel.Codes;
using PolvorApp.SharedKernel.Persistence;

namespace PolvorApp.FederationCatalog.Persistence;

/// <summary>
/// Schema <c>catalog</c>: comparsas, FiringChief assignments, weapon models (design D3) and the
/// Federation's settings with its logo (add-distribution-planning, design D11). The
/// database constraints back up the API's blocking rules against races.
/// </summary>
internal sealed class FederationCatalogDbContext(DbContextOptions<FederationCatalogDbContext> options) : DbContext(options)
{
    public const string Schema = "catalog";

    /// <summary>
    /// Unique index names the services map to problem codes (the only names that do not follow the
    /// default <c>ix_&lt;table&gt;_&lt;columns&gt;</c> pattern).
    /// </summary>
    public const string ComparsaNameIndex = "ix_comparsas_name_key";
    public const string WeaponModelLabelIndex = "ix_weapon_models_label_key";
    public const string WeaponModelCombinationIndex = "ix_weapon_models_combination";

    /// <summary>One stored logo belongs to one comparsa.</summary>
    public const string ComparsaLogoKeyIndex = "ix_comparsas_logo_object_key";

    /// <summary>Primary key of an assignment: a concurrent identical assignment violates it.</summary>
    public const string AssignmentKey = "pk_firing_chief_assignments";

    private const int CodeMaxLength = 16;

    public DbSet<Comparsa> Comparsas => Set<Comparsa>();

    public DbSet<FiringChiefAssignment> Assignments => Set<FiringChiefAssignment>();

    public DbSet<WeaponModel> WeaponModels => Set<WeaponModel>();

    public DbSet<FederationSettings> FederationSettings => Set<FederationSettings>();

    private static string Pistol => EnumCodes.ToCode(WeaponKind.Pistol);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.AddAuditTrail();
        MapComparsas(modelBuilder);
        MapAssignments(modelBuilder);
        MapWeaponModels(modelBuilder);
        MapFederationSettings(modelBuilder);
    }

    private static void MapComparsas(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Comparsa>(comparsa =>
        {
            comparsa.ToTable("comparsas", table =>
            {
                // Adding an enum member changes this list: the model snapshot then asks for a migration.
                table.HasCheckConstraint("ck_comparsas_side", In("side", EnumCodes.All<Side>()));
                table.HasCheckConstraint("ck_comparsas_name_not_blank", "btrim(name) <> ''");

                // A logo is all its columns or none (design D1). Its key is derived from its id under
                // the prefix the orphan sweep owns (LogoStorage.KeyFor), so the sweep never misses one.
                table.HasCheckConstraint("ck_comparsas_logo_complete", LogoColumnsAllNullOrAllSet());
                table.HasCheckConstraint("ck_comparsas_logo_key", LogoKeyDerivedFromId);
                table.HasCheckConstraint("ck_comparsas_logo_size", LogoSizesPositive);
            });
            comparsa.HasKey(c => c.Id);
            comparsa.Property(c => c.Id).ValueGeneratedNever();
            comparsa.Property(c => c.Name).HasMaxLength(Comparsa.NameMaxLength);
            comparsa.Property(c => c.Side).HasConversion(new EnumCodeConverter<Side>()).HasMaxLength(CodeMaxLength);

            // Blocking (spec: Comparsas): names are unique case-insensitively. Npgsql has no
            // expression indexes, so a stored generated column carries the key (design D3).
            // Requires a UTF-8 ctype: with C/POSIX, lower() folds ASCII only (docs/development.md).
            comparsa.Property<string>("NameKey").IsRequired().HasColumnType("text").HasComputedColumnSql("lower(name)", stored: true);
            comparsa.HasIndex("NameKey").IsUnique().HasDatabaseName(ComparsaNameIndex);

            // Columns of the comparsa row, not a table (design D1): comparsas carry no version, and
            // EF writes only changed columns, so a logo upload and a name edit never clobber each other.
            comparsa.OwnsOne(c => c.Logo, logo =>
            {
                MapLogoColumns(logo);
                // The filter is explicit rather than needed: comparsas without a logo are not indexed.
                logo.HasIndex(l => l.ObjectKey).IsUnique().HasFilter("logo_object_key IS NOT NULL").HasDatabaseName(ComparsaLogoKeyIndex);
            });
        });

    /// <summary>
    /// One row, created by the migration (design D11): the check constraint keeps it single, so no
    /// unique index is needed. Its logo follows the comparsa logo rules; the keys are random, so a
    /// stored image is never shared with a comparsa.
    /// </summary>
    private static void MapFederationSettings(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<FederationSettings>(settings =>
        {
            settings.ToTable("federation_settings", table =>
            {
                table.HasCheckConstraint("ck_federation_settings_single", "id = " + Logos.FederationSettings.SingletonId);
                table.HasCheckConstraint("ck_federation_settings_logo_complete", LogoColumnsAllNullOrAllSet());
                table.HasCheckConstraint("ck_federation_settings_logo_key", LogoKeyDerivedFromId);
                table.HasCheckConstraint("ck_federation_settings_logo_size", LogoSizesPositive);

                // Spec: Federation settings (add-federation-settings, design D1).
                table.HasCheckConstraint(
                    "ck_federation_settings_names_not_blank",
                    "btrim(official_name_es) <> '' AND btrim(official_name_ca) <> '' AND btrim(short_name) <> '' AND btrim(sender_name) <> ''");
                table.HasCheckConstraint("ck_federation_settings_sender_name", "sender_name !~ '[[:cntrl:]<>\"@]'");
                table.HasCheckConstraint("ck_federation_settings_website", "website IS NULL OR website LIKE 'https://%'");
                table.HasCheckConstraint(
                    "ck_federation_settings_close_reminder_lead_days",
                    $"close_reminder_lead_days BETWEEN {Logos.FederationSettings.MinCloseReminderLeadDays} AND {Logos.FederationSettings.MaxCloseReminderLeadDays}");
                table.HasCheckConstraint(
                    "ck_federation_settings_milestone_lead_days",
                    $"milestone_lead_days BETWEEN {Logos.FederationSettings.MinMilestoneLeadDays} AND {Logos.FederationSettings.MaxMilestoneLeadDays}");
            });
            settings.HasKey(s => s.Id);
            settings.Property(s => s.Id).ValueGeneratedNever();
            settings.Property(s => s.OfficialNameEs).HasMaxLength(Logos.FederationSettings.OfficialNameMaxLength);
            settings.Property(s => s.OfficialNameCa).HasMaxLength(Logos.FederationSettings.OfficialNameMaxLength);
            settings.Property(s => s.ShortName).HasMaxLength(Logos.FederationSettings.ShortNameMaxLength);
            settings.Property(s => s.ContactEmail).HasMaxLength(Logos.FederationSettings.EmailMaxLength);
            settings.Property(s => s.Website).HasMaxLength(Logos.FederationSettings.WebsiteMaxLength);
            settings.Property(s => s.SenderName).HasMaxLength(Logos.FederationSettings.SenderNameMaxLength);
            settings.Property(s => s.ReplyTo).HasMaxLength(Logos.FederationSettings.EmailMaxLength);
            settings.Property(s => s.Version).IsRowVersion();
            settings.OwnsOne(s => s.Logo, MapLogoColumns);
        });

    private static void MapLogoColumns<TOwner>(OwnedNavigationBuilder<TOwner, ComparsaLogo> logo)
        where TOwner : class
    {
        logo.Property(l => l.Id).HasColumnName("logo_id");
        logo.Property(l => l.ObjectKey).HasColumnName("logo_object_key").HasMaxLength(200);
        logo.Property(l => l.Width).HasColumnName("logo_width");
        logo.Property(l => l.Height).HasColumnName("logo_height");
        logo.Property(l => l.SizeBytes).HasColumnName("logo_size_bytes");
        logo.Property(l => l.UploadedAt).HasColumnName("logo_uploaded_at");
    }

    /// <summary>A logo's key is derived from its id under the prefix the orphan sweep owns (LogoStorage.KeyFor).</summary>
    private static string LogoKeyDerivedFromId =>
        "logo_object_key IS NULL OR logo_object_key = " + Quote(LogoStorage.Prefix) + " || replace(logo_id::text, '-', '') || '.png'";

    private const string LogoSizesPositive = "logo_id IS NULL OR (logo_width > 0 AND logo_height > 0 AND logo_size_bytes > 0)";

    private static string LogoColumnsAllNullOrAllSet()
    {
        string[] columns = ["logo_id", "logo_object_key", "logo_width", "logo_height", "logo_size_bytes", "logo_uploaded_at"];
        return "(" + string.Join(" AND ", columns.Select(c => c + " IS NULL")) + ") OR ("
            + string.Join(" AND ", columns.Select(c => c + " IS NOT NULL")) + ")";
    }

    private static void MapAssignments(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<FiringChiefAssignment>(assignment =>
        {
            assignment.ToTable("firing_chief_assignments");
            assignment.HasKey(a => new { a.ComparsaId, a.UserId });

            // Deleting a comparsa removes its assignments (spec: Deleting comparsas and weapon models).
            assignment.HasOne<Comparsa>().WithMany().HasForeignKey(a => a.ComparsaId).OnDelete(DeleteBehavior.Cascade);

            // Scope lookup on every FiringChief request (BR-12).
            assignment.HasIndex(a => a.UserId);
        });

    private static void MapWeaponModels(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<WeaponModel>(model =>
        {
            model.ToTable("weapon_models", table =>
            {
                table.HasCheckConstraint("ck_weapon_models_kind", In("kind", EnumCodes.All<WeaponKind>()));
                table.HasCheckConstraint("ck_weapon_models_side", "side IS NULL OR " + In("side", EnumCodes.All<Side>()));
                table.HasCheckConstraint("ck_weapon_models_handedness", "handedness IS NULL OR " + In("handedness", EnumCodes.All<Handedness>()));
                table.HasCheckConstraint("ck_weapon_models_size", "size IS NULL OR " + In("size", EnumCodes.All<WeaponSize>()));
                table.HasCheckConstraint("ck_weapon_models_label_not_blank", "btrim(label) <> ''");

                // The attribute rule (spec: Weapon models (BR-07)), blocking. Any kind may be rentable.
                table.HasCheckConstraint(
                    "ck_weapon_models_attributes",
                    "kind = " + Quote(Pistol) + " OR (side IS NOT NULL AND handedness IS NOT NULL AND size IS NOT NULL)");
            });
            model.HasKey(m => m.Id);
            model.Property(m => m.Id).ValueGeneratedNever();
            model.Property(m => m.Kind).HasConversion(new EnumCodeConverter<WeaponKind>()).HasMaxLength(CodeMaxLength);
            model.Property(m => m.Side).HasConversion(new EnumCodeConverter<Side>()).HasMaxLength(CodeMaxLength);
            model.Property(m => m.Handedness).HasConversion(new EnumCodeConverter<Handedness>()).HasMaxLength(CodeMaxLength);
            model.Property(m => m.Size).HasConversion(new EnumCodeConverter<WeaponSize>()).HasMaxLength(CodeMaxLength);
            model.Property(m => m.Label).HasMaxLength(WeaponModel.LabelMaxLength);

            model.Property<string>("LabelKey").IsRequired().HasColumnType("text").HasComputedColumnSql("lower(label)", stored: true);
            model.HasIndex("LabelKey").IsUnique().HasDatabaseName(WeaponModelLabelIndex);

            // Pistols have no attributes to compare, so only trabucos and arcabuces are unique by combination.
            model.HasIndex(m => new { m.Kind, m.Side, m.Handedness, m.Size })
                .IsUnique()
                .HasFilter("kind <> " + Quote(Pistol))
                .HasDatabaseName(WeaponModelCombinationIndex);
        });

    private static string Quote(string code) => "'" + code + "'";

    private static string In(string column, IEnumerable<string> codes) =>
        column + " IN (" + string.Join(", ", codes.Select(Quote)) + ")";
}

/// <summary>Lets <c>dotnet ef migrations add</c> build the model; it never connects.</summary>
internal sealed class FederationCatalogDbContextDesignTimeFactory : IDesignTimeDbContextFactory<FederationCatalogDbContext>
{
    public FederationCatalogDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<FederationCatalogDbContext>()
            .UseModuleDatabase("Host=design-time-only", FederationCatalogDbContext.Schema)
            .Options);
}
