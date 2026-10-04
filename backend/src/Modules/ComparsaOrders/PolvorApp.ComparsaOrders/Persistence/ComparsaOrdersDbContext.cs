using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.ComparsaOrders.Entries;
using PolvorApp.ComparsaOrders.Loans;
using PolvorApp.ComparsaOrders.Orders;
using PolvorApp.Notifications.Contracts;
using PolvorApp.SharedKernel.Auditing;
using PolvorApp.SharedKernel.Codes;
using PolvorApp.SharedKernel.Persistence;

namespace PolvorApp.ComparsaOrders.Persistence;

/// <summary>
/// Schema <c>orders</c>: comparsa orders, their edition entries and weapon loans (design D2). The
/// database constraints back up the API's blocking rules against races. The cross-schema foreign
/// keys are added by the migration (Modules README).
/// </summary>
internal sealed class ComparsaOrdersDbContext(DbContextOptions<ComparsaOrdersDbContext> options) : DbContext(options)
{
    public const string Schema = "orders";

    /// <summary>At most one order per comparsa and edition; violating it is <c>orders.alreadyPrepared</c>.</summary>
    public const string OrderIndex = "ux_comparsa_orders_edition_comparsa";

    /// <summary>At most one entry per arquebusier and edition, while the entry is linked to the registry.</summary>
    public const string EntryIndex = "ux_edition_entries_edition_arquebusier";

    /// <summary>A <c>RESERVE</c> entry has no powder, caps, weapon or flask (BR-05).</summary>
    public const string ReserveCheck = "ck_edition_entries_reserve";

    /// <summary>The caps type is set exactly when there are caps boxes.</summary>
    public const string CapsTypeCheck = "ck_edition_entries_caps_type";

    /// <summary>A rental model exactly for the weapon source <c>RENTAL</c>.</summary>
    public const string RentalCheck = "ck_edition_entries_rental";

    /// <summary>No owned weapon, or its copy, unless the weapon source is <c>OWNED</c>.</summary>
    public const string OwnedCheck = "ck_edition_entries_owned";

    /// <summary>An external owner's loan names no registered weapon or comparsa.</summary>
    public const string ExternalLoanCheck = "ck_weapon_loans_external";

    /// <summary>A registered owner's loan names the owner's comparsa.</summary>
    public const string RegisteredLoanCheck = "ck_weapon_loans_registered";

    /// <summary>Powder is 0, 1 or 2 kg (BR-05).</summary>
    public const string PowderCheck = "ck_edition_entries_powder_kg";

    /// <summary>Caps boxes are 0 to 99.</summary>
    public const string CapsBoxesCheck = "ck_edition_entries_caps_boxes";

    /// <summary>An entry linked to the registry holds its identity copy (design D3).</summary>
    public const string CopyCheck = "ck_edition_entries_copy";

    /// <summary>A return reason exactly while the order is returned.</summary>
    public const string ReturnReasonCheck = "ck_comparsa_orders_return_reason";

    /// <summary>A submission is either attested by a FiringChief or made by an Admin, never both.</summary>
    public const string SubmissionCheck = "ck_comparsa_orders_submission";

    // Cross-schema foreign keys, added by the initial migration (design D2).
    public const string EditionForeignKey = "fk_comparsa_orders_edition";
    public const string ComparsaForeignKey = "fk_comparsa_orders_comparsa";
    public const string RentalModelForeignKey = "fk_edition_entries_rental_weapon_model";
    public const string OwnedWeaponModelForeignKey = "fk_edition_entries_owned_weapon_model";
    public const string ArquebusierForeignKey = "fk_edition_entries_arquebusier";
    public const string OwnedWeaponForeignKey = "fk_edition_entries_owned_weapon";
    public const string LoanWeaponModelForeignKey = "fk_weapon_loans_weapon_model";
    public const string LoanComparsaForeignKey = "fk_weapon_loans_lender_comparsa";
    public const string LoanOwnedWeaponForeignKey = "fk_weapon_loans_lender_owned_weapon";

    private const int CodeMaxLength = 16;

    public DbSet<ComparsaOrder> Orders => Set<ComparsaOrder>();

    public DbSet<EditionEntry> Entries => Set<EditionEntry>();

    public DbSet<WeaponLoan> Loans => Set<WeaponLoan>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.AddAuditTrail();

        // Events that cause notifications commit with the change (add-notifications, design D2).
        modelBuilder.AddNotificationOutbox();
        MapOrders(modelBuilder);
        MapEntries(modelBuilder);
        MapLoans(modelBuilder);
    }

    private static void MapOrders(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<ComparsaOrder>(order =>
        {
            order.ToTable("comparsa_orders", table =>
            {
                // Adding an enum member changes this list: the model snapshot then asks for a migration.
                table.HasCheckConstraint("ck_comparsa_orders_status", In("status", EnumCodes.All<OrderStatus>()));
                table.HasCheckConstraint(
                    ReturnReasonCheck,
                    "(status = " + Code(OrderStatus.Returned) + ") = (return_reason IS NOT NULL) AND (return_reason IS NULL OR btrim(return_reason) <> '')");
                table.HasCheckConstraint(SubmissionCheck, "NOT (attested AND submitted_by_admin)");
            });
            order.HasKey(o => o.Id);
            order.Property(o => o.Id).ValueGeneratedNever();
            order.Property(o => o.Status).HasConversion(new EnumCodeConverter<OrderStatus>()).HasMaxLength(CodeMaxLength);
            order.Property(o => o.ReturnReason).HasMaxLength(ComparsaOrder.ReturnReasonMaxLength);
            order.Property(o => o.Version).IsRowVersion();
            order.HasIndex(o => new { o.EditionId, o.ComparsaId }).IsUnique().HasDatabaseName(OrderIndex);

            // Target of the entries' composite key, so an entry's edition always equals its order's.
            order.HasAlternateKey(o => new { o.Id, o.EditionId });

            // The catalogue usage check and per-comparsa history look orders up by comparsa.
            order.HasIndex(o => o.ComparsaId);

            // The pre-fill and the first-year rule look orders up by year.
            order.HasIndex(o => o.EditionYear);
        });

    private static void MapEntries(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<EditionEntry>(entry =>
        {
            entry.ToTable("edition_entries", table =>
            {
                table.HasCheckConstraint("ck_edition_entries_status", In("status", EnumCodes.All<ArquebusierStatus>()));
                table.HasCheckConstraint("ck_edition_entries_weapon_source", In("weapon_source", EnumCodes.All<WeaponSource>()));
                table.HasCheckConstraint("ck_edition_entries_flask", In("flask", EnumCodes.All<FlaskOption>()));
                table.HasCheckConstraint("ck_edition_entries_caps_type_code", "caps_type IS NULL OR " + In("caps_type", EnumCodes.All<CapsType>()));
                table.HasCheckConstraint(PowderCheck, "powder_kg BETWEEN 0 AND " + EditionEntry.MaxPowderKg);
                table.HasCheckConstraint(CapsBoxesCheck, "caps_boxes BETWEEN 0 AND " + EditionEntry.MaxCapsBoxes);
                table.HasCheckConstraint(
                    CopyCheck,
                    "arquebusier_id IS NULL OR (first_name IS NOT NULL AND last_name IS NOT NULL AND national_id IS NOT NULL AND federation_id IS NOT NULL)");
                table.HasCheckConstraint(CapsTypeCheck, "(caps_boxes = 0) = (caps_type IS NULL)");
                table.HasCheckConstraint(
                    ReserveCheck,
                    "status <> " + Code(ArquebusierStatus.Reserve)
                    + " OR (powder_kg = 0 AND caps_boxes = 0 AND weapon_source = " + Code(WeaponSource.None) + " AND flask = " + Code(FlaskOption.None) + ")");
                table.HasCheckConstraint(RentalCheck, "(weapon_source = " + Code(WeaponSource.Rental) + ") = (rental_weapon_model_id IS NOT NULL)");
                table.HasCheckConstraint(
                    OwnedCheck,
                    "weapon_source = " + Code(WeaponSource.Owned)
                    + " OR (owned_weapon_id IS NULL AND owned_weapon_model_id IS NULL AND owned_weapon_number IS NULL AND owned_weapon_guide_number IS NULL)");
            });
            entry.HasKey(e => e.Id);
            entry.Property(e => e.Id).ValueGeneratedNever();
            entry.Property(e => e.Status).HasConversion(new EnumCodeConverter<ArquebusierStatus>()).HasMaxLength(CodeMaxLength);
            entry.Property(e => e.CapsType).HasConversion(new EnumCodeConverter<CapsType>()).HasMaxLength(CodeMaxLength);
            entry.Property(e => e.WeaponSource).HasConversion(new EnumCodeConverter<WeaponSource>()).HasMaxLength(CodeMaxLength);
            entry.Property(e => e.Flask).HasConversion(new EnumCodeConverter<FlaskOption>()).HasMaxLength(CodeMaxLength);
            entry.Property(e => e.PowderKg).HasColumnType("smallint");
            entry.Property(e => e.CapsBoxes).HasColumnType("smallint");
            entry.Property(e => e.FirstName).HasMaxLength(EditionEntry.NameMaxLength);
            entry.Property(e => e.LastName).HasMaxLength(EditionEntry.NameMaxLength);
            entry.Property(e => e.NationalId).HasMaxLength(EditionEntry.NationalIdLength);
            entry.Property(e => e.OwnedWeaponNumber).HasMaxLength(EditionEntry.WeaponNumberMaxLength);
            entry.Property(e => e.OwnedWeaponGuideNumber).HasMaxLength(EditionEntry.WeaponNumberMaxLength);
            entry.Property(e => e.Version).IsRowVersion();
            // Composite, so the denormalised edition always equals the order's (the unique index relies on it).
            entry.HasOne<ComparsaOrder>().WithMany()
                .HasForeignKey(e => new { e.OrderId, e.EditionId })
                .HasPrincipalKey(o => new { o.Id, o.EditionId })
                .OnDelete(DeleteBehavior.Cascade);
            entry.HasIndex(e => new { e.EditionId, e.ArquebusierId }).IsUnique()
                .HasFilter("arquebusier_id IS NOT NULL").HasDatabaseName(EntryIndex);

            // ON DELETE SET NULL from the registry, the pre-fill, the first-year rule and the deletion
            // impact look these up; the catalogue deletes check the model columns. Partial, because
            // history rows leave them null.
            entry.HasIndex(e => e.ArquebusierId).HasFilter("arquebusier_id IS NOT NULL");
            entry.HasIndex(e => e.OwnedWeaponId).HasFilter("owned_weapon_id IS NOT NULL");
            entry.HasIndex(e => e.RentalWeaponModelId).HasFilter("rental_weapon_model_id IS NOT NULL");
            entry.HasIndex(e => e.OwnedWeaponModelId).HasFilter("owned_weapon_model_id IS NOT NULL");
        });

    private static void MapLoans(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<WeaponLoan>(loan =>
        {
            loan.ToTable("weapon_loans", table =>
            {
                table.HasCheckConstraint("ck_weapon_loans_lender_kind", In("lender_kind", EnumCodes.All<LenderKind>()));
                table.HasCheckConstraint(
                    ExternalLoanCheck,
                    "lender_kind <> " + Code(LenderKind.External) + " OR (lender_owned_weapon_id IS NULL AND lender_comparsa_id IS NULL)");
                table.HasCheckConstraint(
                    RegisteredLoanCheck,
                    "lender_kind <> " + Code(LenderKind.Arquebusier) + " OR lender_comparsa_id IS NOT NULL");
            });
            loan.HasKey(l => l.Id);
            loan.Property(l => l.Id).ValueGeneratedNever();
            loan.Property(l => l.LenderKind).HasConversion(new EnumCodeConverter<LenderKind>()).HasMaxLength(CodeMaxLength);
            loan.Property(l => l.LenderFirstName).HasMaxLength(EditionEntry.NameMaxLength);
            loan.Property(l => l.LenderLastName).HasMaxLength(EditionEntry.NameMaxLength);
            loan.Property(l => l.LenderNationalId).HasMaxLength(EditionEntry.NationalIdLength);
            loan.Property(l => l.WeaponNumber).HasMaxLength(EditionEntry.WeaponNumberMaxLength);
            loan.Property(l => l.OwnershipGuideNumber).HasMaxLength(EditionEntry.WeaponNumberMaxLength);
            loan.HasOne<EditionEntry>().WithOne().HasForeignKey<WeaponLoan>(l => l.EntryId).OnDelete(DeleteBehavior.Cascade);

            // The lent-out list and ON DELETE SET NULL look loans up by the lender's weapon; the entry id
            // is included so the list reaches the borrower's entry from the index.
            loan.HasIndex(l => l.LenderOwnedWeaponId).HasFilter("lender_owned_weapon_id IS NOT NULL").IncludeProperties(l => l.EntryId);

            // The catalogue deletes check these.
            loan.HasIndex(l => l.WeaponModelId).HasFilter("weapon_model_id IS NOT NULL");
            loan.HasIndex(l => l.LenderComparsaId).HasFilter("lender_comparsa_id IS NOT NULL");
        });

    private static string Code<TEnum>(TEnum value)
        where TEnum : struct, Enum => Quote(EnumCodes.ToCode(value));

    private static string Quote(string code) => "'" + code + "'";

    private static string In(string column, IEnumerable<string> codes) =>
        column + " IN (" + string.Join(", ", codes.Select(Quote)) + ")";
}

/// <summary>Lets <c>dotnet ef migrations add</c> build the model; it never connects.</summary>
internal sealed class ComparsaOrdersDbContextDesignTimeFactory : IDesignTimeDbContextFactory<ComparsaOrdersDbContext>
{
    public ComparsaOrdersDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<ComparsaOrdersDbContext>()
            .UseModuleDatabase("Host=design-time-only", ComparsaOrdersDbContext.Schema)
            .Options);
}
