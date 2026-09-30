using Microsoft.EntityFrameworkCore;

namespace PolvorApp.SharedKernel.Auditing;

/// <summary>Mapping of <see cref="AuditEntry"/> shared by every module DbContext (design D3).</summary>
public static class AuditTrailModel
{
    public const string Schema = "audit";
    public const string Table = "audit_entries";

    /// <summary>
    /// Maps the audit trail table. Only the audit-privacy module passes <paramref name="ownsTable"/>;
    /// every other module excludes the table from its migrations.
    /// </summary>
    public static ModelBuilder AddAuditTrail(this ModelBuilder modelBuilder, bool ownsTable = false)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<AuditEntry>(entry =>
        {
            entry.ToTable(Table, Schema, table =>
            {
                if (!ownsTable)
                {
                    table.ExcludeFromMigrations();
                }
            });
            entry.HasKey(e => e.Id);
            entry.Property(e => e.Id).ValueGeneratedNever();
            entry.Property(e => e.Action).HasMaxLength(100);
            entry.Property(e => e.EntityType).HasMaxLength(100);
            entry.Property(e => e.EntityId).HasMaxLength(100);
            entry.Property(e => e.TraceId).HasMaxLength(64);
            entry.Property(e => e.Data).HasColumnType("jsonb");
            // Viewer filters (#15): time range, entity, actor and comparsa, newest first.
            entry.HasIndex(e => e.OccurredAt);
            entry.HasIndex(e => new { e.EntityType, e.EntityId });
            entry.HasIndex(e => new { e.ActorUserId, e.OccurredAt }).HasFilter("actor_user_id IS NOT NULL");
            entry.HasIndex(e => new { e.ComparsaId, e.OccurredAt }).HasFilter("comparsa_id IS NOT NULL");
        });

        return modelBuilder;
    }
}
