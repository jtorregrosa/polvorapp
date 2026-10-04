using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using PolvorApp.FestivalEditions.Contracts;
using PolvorApp.FestivalEditions.Editions;
using PolvorApp.Notifications.Contracts;
using PolvorApp.SharedKernel.Auditing;
using PolvorApp.SharedKernel.Codes;
using PolvorApp.SharedKernel.Persistence;

namespace PolvorApp.FestivalEditions.Persistence;

/// <summary>
/// Schema <c>editions</c>: festival editions, their rental models and milestones (design D1). The
/// database constraints back up the API's blocking rules against races.
/// </summary>
internal sealed class FestivalEditionsDbContext(DbContextOptions<FestivalEditionsDbContext> options) : DbContext(options)
{
    public const string Schema = "editions";

    /// <summary>Unique year; violating it is <c>editions.yearTaken</c>.</summary>
    public const string YearIndex = "ix_festival_editions_year";

    /// <summary>At most one edition in progress (design D3); violating it is <c>editions.anotherInProgress</c>.</summary>
    public const string InProgressIndex = "ux_festival_editions_in_progress";

    /// <summary>Foreign key to <c>catalog.weapon_models</c>, added by the initial migration (design D1).</summary>
    public const string WeaponModelForeignKey = "fk_edition_weapon_models_catalog_weapon_models";

    /// <summary>Only an edition in progress has open orders.</summary>
    public const string OrdersOpenCheck = "ck_festival_editions_orders_open_in_progress";

    /// <summary>Precision of the prices: at most 9999.99 euros (spec: Edition prices).</summary>
    public const int PricePrecision = 6;
    public const int PriceScale = 2;

    private const int CodeMaxLength = 16;

    private static readonly string[] PriceColumns = ["powder_per_kg", "caps_box", "weapon_rental", "flask_rental"];

    public DbSet<FestivalEdition> Editions => Set<FestivalEdition>();

    public DbSet<EditionWeaponModel> EditionWeaponModels => Set<EditionWeaponModel>();

    public DbSet<CalendarMilestone> Milestones => Set<CalendarMilestone>();

    private static string InProgress => Quote(EnumCodes.ToCode(EditionStatus.InProgress));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.AddAuditTrail();

        // Events that cause notifications commit with the change (add-notifications, design D2).
        modelBuilder.AddNotificationOutbox();
        MapEditions(modelBuilder);
        MapWeaponModels(modelBuilder);
        MapMilestones(modelBuilder);
    }

    private static void MapEditions(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<FestivalEdition>(edition =>
        {
            edition.ToTable("festival_editions", table =>
            {
                // Adding an enum member changes this list: the model snapshot then asks for a migration.
                table.HasCheckConstraint("ck_festival_editions_status", In("status", EnumCodes.All<EditionStatus>()));
                table.HasCheckConstraint("ck_festival_editions_year", "year BETWEEN " + FestivalEdition.MinYear + " AND " + FestivalEdition.MaxYear);
                table.HasCheckConstraint("ck_festival_editions_festival_dates", "festival_ends_on >= festival_starts_on");
                table.HasCheckConstraint(
                    "ck_festival_editions_orders_window",
                    "orders_open_on IS NULL OR orders_close_on IS NULL OR orders_close_on >= orders_open_on");
                table.HasCheckConstraint(OrdersOpenCheck, "NOT orders_open OR status = " + InProgress);
                foreach (var price in PriceColumns)
                {
                    table.HasCheckConstraint("ck_festival_editions_" + price, price + " IS NULL OR " + price + " >= 0");
                }
            });
            edition.HasKey(e => e.Id);
            edition.Property(e => e.Id).ValueGeneratedNever();
            edition.Property(e => e.Status).HasConversion(new EnumCodeConverter<EditionStatus>()).HasMaxLength(CodeMaxLength);
            edition.Property(e => e.PowderPerKg).HasPrecision(PricePrecision, PriceScale);
            edition.Property(e => e.CapsBox).HasPrecision(PricePrecision, PriceScale);
            edition.Property(e => e.WeaponRental).HasPrecision(PricePrecision, PriceScale);
            edition.Property(e => e.FlaskRental).HasPrecision(PricePrecision, PriceScale);
            edition.Property(e => e.Version).IsRowVersion();
            edition.HasIndex(e => e.Year).IsUnique().HasDatabaseName(YearIndex);

            // Every indexed row has the same value, so at most one edition is in progress (design D3).
            edition.HasIndex(e => e.Status).IsUnique().HasFilter("status = " + InProgress).HasDatabaseName(InProgressIndex);
        });

    private static void MapWeaponModels(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<EditionWeaponModel>(model =>
        {
            // The foreign key to catalog.weapon_models is added by the migration (cross-schema, design D1).
            model.ToTable("edition_weapon_models");
            model.HasKey(m => new { m.EditionId, m.WeaponModelId });
            model.HasOne<FestivalEdition>().WithMany().HasForeignKey(m => m.EditionId).OnDelete(DeleteBehavior.Cascade);

            // The catalogue usage check looks models up by id.
            model.HasIndex(m => m.WeaponModelId);
        });

    private static void MapMilestones(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<CalendarMilestone>(milestone =>
        {
            milestone.ToTable("calendar_milestones", table =>
                table.HasCheckConstraint("ck_calendar_milestones_title_not_blank", "btrim(title) <> ''"));
            milestone.HasKey(m => m.Id);
            milestone.Property(m => m.Id).ValueGeneratedNever();
            milestone.Property(m => m.Title).HasMaxLength(CalendarMilestone.TitleMaxLength);
            milestone.HasOne<FestivalEdition>().WithMany().HasForeignKey(m => m.EditionId).OnDelete(DeleteBehavior.Cascade);
            milestone.HasIndex(m => new { m.EditionId, m.Date });
        });

    private static string Quote(string code) => "'" + code + "'";

    private static string In(string column, IEnumerable<string> codes) =>
        column + " IN (" + string.Join(", ", codes.Select(Quote)) + ")";
}

/// <summary>Lets <c>dotnet ef migrations add</c> build the model; it never connects.</summary>
internal sealed class FestivalEditionsDbContextDesignTimeFactory : IDesignTimeDbContextFactory<FestivalEditionsDbContext>
{
    public FestivalEditionsDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<FestivalEditionsDbContext>()
            .UseModuleDatabase("Host=design-time-only", FestivalEditionsDbContext.Schema)
            .Options);
}
