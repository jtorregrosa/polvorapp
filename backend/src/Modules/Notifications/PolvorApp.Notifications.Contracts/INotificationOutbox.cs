using Microsoft.EntityFrameworkCore;
using PolvorApp.SharedKernel.Codes;
using PolvorApp.SharedKernel.Persistence;

namespace PolvorApp.Notifications.Contracts;

/// <summary>
/// Records the events that cause notifications in the caller's unit of work (design D2): an event is
/// stored by the caller's next <c>SaveChanges</c>, in the same transaction as the change, so it exists
/// exactly when the change was saved. The notifications module sends the emails in the background.
/// </summary>
public interface INotificationOutbox
{
    /// <summary>
    /// Adds <paramref name="notificationEvent"/> to <paramref name="context"/>, which must map the outbox
    /// with <see cref="NotificationOutboxModel.AddNotificationOutbox"/>.
    /// </summary>
    void Record(DbContext context, NotificationEvent notificationEvent);
}

/// <summary>
/// One row of the notification outbox (<c>notifications.notification_events</c>). The notifications
/// module owns the table; modules that record events map it too, like the audit trail. Identifiers only.
/// Create rows only through <see cref="INotificationOutbox"/>.
/// </summary>
public sealed class NotificationEventEntry
{
    public required Guid Id { get; init; }

    public required NotificationEventType Type { get; init; }

    public required Guid EditionId { get; init; }

    public Guid? ComparsaId { get; init; }

    public Guid? OrderId { get; init; }

    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>When the notifications module turned the event into deliveries; null while waiting.</summary>
    public DateTimeOffset? ProcessedAt { get; set; }
}

/// <summary>Mapping of <see cref="NotificationEventEntry"/> shared by the modules that record events (design D2).</summary>
public static class NotificationOutboxModel
{
    public const string Schema = "notifications";
    public const string Table = "notification_events";

    private const int CodeMaxLength = 32;

    /// <summary>
    /// Maps the outbox table. Only the notifications module passes <paramref name="ownsTable"/>; every
    /// other module leaves the table out of its migrations.
    /// </summary>
    public static ModelBuilder AddNotificationOutbox(this ModelBuilder modelBuilder, bool ownsTable = false)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<NotificationEventEntry>(entry =>
        {
            entry.ToTable(Table, Schema, table =>
            {
                if (ownsTable)
                {
                    table.HasCheckConstraint(
                        "ck_notification_events_type",
                        "type IN (" + string.Join(", ", EnumCodes.All<NotificationEventType>().Select(code => "'" + code + "'")) + ")");
                }
                else
                {
                    table.ExcludeFromMigrations();
                }
            });
            entry.HasKey(e => e.Id);
            entry.Property(e => e.Id).ValueGeneratedNever();
            entry.Property(e => e.Type).HasConversion(new EnumCodeConverter<NotificationEventType>()).HasMaxLength(CodeMaxLength);

            // The dispatcher picks the events still waiting, oldest first.
            entry.HasIndex(e => e.OccurredAt).HasFilter("processed_at IS NULL").HasDatabaseName("ix_notification_events_pending");
        });

        return modelBuilder;
    }
}
