using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using PolvorApp.Notifications.Contracts;
using PolvorApp.SharedKernel.Auditing;
using PolvorApp.SharedKernel.Codes;
using PolvorApp.SharedKernel.Persistence;

namespace PolvorApp.Notifications.Persistence;

/// <summary>
/// Schema <c>notifications</c>: the event outbox other modules write to (design D2), the deliveries
/// (D3), the opt-outs (D4) and the scheduled runs (D5). No cross-schema foreign keys: users are never deleted, and a delivery
/// about an edition, order or milestone that went away is skipped when it is sent.
/// </summary>
internal sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options) : DbContext(options)
{
    public const string Schema = NotificationOutboxModel.Schema;

    /// <summary>One delivery per recipient and topic: the authority against duplicates (design D3).</summary>
    public const string DeliveryTopicIndex = "ux_notification_deliveries_user_topic";

    private const int CodeMaxLength = 32;

    public DbSet<NotificationEventEntry> Events => Set<NotificationEventEntry>();

    public DbSet<NotificationDelivery> Deliveries => Set<NotificationDelivery>();

    public DbSet<NotificationOptOut> OptOuts => Set<NotificationOptOut>();

    public DbSet<NotificationRun> Runs => Set<NotificationRun>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.AddAuditTrail();
        modelBuilder.AddNotificationOutbox(ownsTable: true);

        modelBuilder.Entity<NotificationDelivery>(delivery =>
        {
            delivery.ToTable("notification_deliveries", table =>
            {
                table.HasCheckConstraint("ck_notification_deliveries_kind", In("kind", EnumCodes.All<NotificationKind>()));
                table.HasCheckConstraint("ck_notification_deliveries_status", In("status", EnumCodes.All<DeliveryStatus>()));
            });
            delivery.HasKey(d => d.Id);
            delivery.Property(d => d.Id).ValueGeneratedNever();
            delivery.Property(d => d.Kind).HasConversion(new EnumCodeConverter<NotificationKind>()).HasMaxLength(CodeMaxLength);
            delivery.Property(d => d.Status).HasConversion(new EnumCodeConverter<DeliveryStatus>()).HasMaxLength(CodeMaxLength);
            delivery.Property(d => d.Template).HasMaxLength(NotificationDelivery.TemplateMaxLength);
            delivery.Property(d => d.Topic).HasMaxLength(NotificationDelivery.TopicMaxLength);
            delivery.Property(d => d.Data).HasColumnType("jsonb");
            delivery.Property(d => d.LastError).HasMaxLength(NotificationDelivery.LastErrorMaxLength);
            delivery.HasIndex(d => new { d.UserId, d.Topic }).IsUnique().HasDatabaseName(DeliveryTopicIndex);

            // The sender picks the pending deliveries that are due; the clean-up finds old ones by date.
            delivery.HasIndex(d => d.NextAttemptAt).HasFilter("status = 'PENDING'").HasDatabaseName("ix_notification_deliveries_due");
            delivery.HasIndex(d => d.CreatedAt);
        });

        modelBuilder.Entity<NotificationOptOut>(optOut =>
        {
            optOut.ToTable("notification_opt_outs", table =>
                table.HasCheckConstraint("ck_notification_opt_outs_kind", In("kind", EnumCodes.All<NotificationKind>())));
            optOut.HasKey(o => new { o.UserId, o.Kind });
            optOut.Property(o => o.Kind).HasConversion(new EnumCodeConverter<NotificationKind>()).HasMaxLength(CodeMaxLength);
        });

        modelBuilder.Entity<NotificationRun>(run =>
        {
            run.ToTable("notification_runs");
            run.HasKey(r => new { r.Job, r.Period });
            run.Property(r => r.Job).HasMaxLength(NotificationRun.JobMaxLength);
            run.Property(r => r.Period).HasMaxLength(NotificationRun.PeriodMaxLength);
        });
    }

    private static string Quote(string code) => "'" + code + "'";

    private static string In(string column, IEnumerable<string> codes) =>
        column + " IN (" + string.Join(", ", codes.Select(Quote)) + ")";
}

/// <summary>Lets <c>dotnet ef migrations add</c> build the model; it never connects.</summary>
internal sealed class NotificationsDbContextDesignTimeFactory : IDesignTimeDbContextFactory<NotificationsDbContext>
{
    public NotificationsDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseModuleDatabase("Host=design-time-only", NotificationsDbContext.Schema)
            .Options);
}
