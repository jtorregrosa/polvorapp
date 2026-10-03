using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using PolvorApp.Distribution.Contracts;
using PolvorApp.Distribution.Days;
using PolvorApp.Distribution.Proxies;
using PolvorApp.SharedKernel.Auditing;
using PolvorApp.SharedKernel.Codes;
using PolvorApp.SharedKernel.Persistence;

namespace PolvorApp.Distribution.Persistence;

/// <summary>
/// Schema <c>distribution</c>: distribution days, their slots and the pickup proxies (design D2). The
/// database constraints back up the API's blocking rules against races. The cross-schema foreign
/// keys are added by the migration (Modules README): to editions and comparsas with
/// <c>NO ACTION</c>, and to edition entries with <c>CASCADE</c>, because a proxy means nothing without
/// both entries (BR-14).
/// </summary>
internal sealed class DistributionDbContext(DbContextOptions<DistributionDbContext> options) : DbContext(options)
{
    public const string Schema = "distribution";

    /// <summary>At most one day of each type per edition; violating it is <c>distribution.alreadyPlanned</c>.</summary>
    public const string DayTypeIndex = "ux_distributions_edition_type";

    /// <summary>At most one proxy per holder and type; violating it is <c>proxies.alreadyAuthorised</c>.</summary>
    public const string ProxyHolderIndex = "ux_pickup_proxies_holder_type";

    public const string LocationCheck = "ck_distributions_location_not_blank";
    public const string NotHolderCheck = "ck_pickup_proxies_not_holder";

    public const string DayEditionForeignKey = "fk_distributions_edition";
    public const string SlotComparsaForeignKey = "fk_distribution_slots_comparsa";
    public const string ProxyEditionForeignKey = "fk_pickup_proxies_edition";
    public const string ProxyComparsaForeignKey = "fk_pickup_proxies_comparsa";
    public const string ProxyHolderEntryForeignKey = "fk_pickup_proxies_holder_entry";
    public const string ProxyProxyEntryForeignKey = "fk_pickup_proxies_proxy_entry";

    private const int CodeMaxLength = 16;

    public DbSet<DistributionDay> Days => Set<DistributionDay>();

    public DbSet<DistributionSlot> Slots => Set<DistributionSlot>();

    public DbSet<PickupProxy> Proxies => Set<PickupProxy>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.AddAuditTrail();

        modelBuilder.Entity<DistributionDay>(day =>
        {
            day.ToTable("distributions", table =>
            {
                table.HasCheckConstraint("ck_distributions_type", In("type", EnumCodes.All<DistributionType>()));
                table.HasCheckConstraint(LocationCheck, "btrim(location) <> ''");
            });
            day.HasKey(d => d.Id);
            day.Property(d => d.Id).ValueGeneratedNever();
            day.Property(d => d.Type).HasConversion(new EnumCodeConverter<DistributionType>()).HasMaxLength(CodeMaxLength);
            day.Property(d => d.Location).HasMaxLength(DistributionDay.LocationMaxLength);
            day.Property(d => d.Version).IsRowVersion();
            day.HasIndex(d => new { d.EditionId, d.Type }).IsUnique().HasDatabaseName(DayTypeIndex);
            day.HasMany(d => d.Slots).WithOne().HasForeignKey(s => s.DistributionId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DistributionSlot>(slot =>
        {
            slot.ToTable("distribution_slots");
            slot.HasKey(s => new { s.DistributionId, s.ComparsaId });

            // The catalogue asks whether a comparsa has a slot before deleting it.
            slot.HasIndex(s => s.ComparsaId);
        });

        modelBuilder.Entity<PickupProxy>(proxy =>
        {
            proxy.ToTable("pickup_proxies", table =>
            {
                table.HasCheckConstraint("ck_pickup_proxies_type", In("type", EnumCodes.All<DistributionType>()));
                table.HasCheckConstraint(NotHolderCheck, "holder_entry_id <> proxy_entry_id");
            });
            proxy.HasKey(p => p.Id);
            proxy.Property(p => p.Id).ValueGeneratedNever();
            proxy.Property(p => p.Type).HasConversion(new EnumCodeConverter<DistributionType>()).HasMaxLength(CodeMaxLength);
            proxy.HasIndex(p => new { p.HolderEntryId, p.Type }).IsUnique().HasDatabaseName(ProxyHolderIndex);

            // Listing per comparsa (BR-12), the absence rules and the cascade from the proxy's entry.
            proxy.HasIndex(p => new { p.EditionId, p.ComparsaId });
            proxy.HasIndex(p => p.ProxyEntryId);

            // The catalogue's deletion veto and the foreign key's check on a comparsa deletion.
            proxy.HasIndex(p => p.ComparsaId);
        });
    }

    private static string Quote(string code) => "'" + code + "'";

    private static string In(string column, IEnumerable<string> codes) =>
        column + " IN (" + string.Join(", ", codes.Select(Quote)) + ")";
}

/// <summary>Lets <c>dotnet ef migrations add</c> build the model; it never connects.</summary>
internal sealed class DistributionDbContextDesignTimeFactory : IDesignTimeDbContextFactory<DistributionDbContext>
{
    public DistributionDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<DistributionDbContext>()
            .UseModuleDatabase("Host=design-time-only", DistributionDbContext.Schema)
            .Options);
}
