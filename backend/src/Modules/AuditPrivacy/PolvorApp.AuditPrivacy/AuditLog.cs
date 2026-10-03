using PolvorApp.AuditPrivacy.Persistence;
using PolvorApp.SharedKernel.Auditing;

namespace PolvorApp.AuditPrivacy;

/// <inheritdoc />
internal sealed class AuditLog(AuditDbContext db, IAuditTrail trail) : IAuditLog
{
    public async Task RecordAsync(AuditRecord record, CancellationToken cancellationToken)
    {
        trail.Record(db, record);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // Not saved: drop the entry, so nothing else in this scope saves it later by surprise.
            db.ChangeTracker.Clear();
            throw;
        }
    }
}
