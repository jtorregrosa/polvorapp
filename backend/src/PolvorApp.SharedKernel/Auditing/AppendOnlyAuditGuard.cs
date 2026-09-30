using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace PolvorApp.SharedKernel.Auditing;

/// <summary>
/// Refuses to save a modified or deleted <see cref="AuditEntry"/> (spec: Audit trail is
/// append-only). It only sees the change tracker: bulk <c>ExecuteUpdate</c>/<c>ExecuteDelete</c>
/// and raw SQL bypass it, so an architecture test forbids them on the audit trail. A database-level
/// guard (trigger or low-privilege runtime role) is decided with GDPR erasure (UC-26) in change #15.
/// </summary>
public sealed class AppendOnlyAuditGuard : SaveChangesInterceptor
{
    public static readonly AppendOnlyAuditGuard Instance = new();

    private AppendOnlyAuditGuard()
    {
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        Check(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        Check(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private static void Check(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var tampered = context.ChangeTracker.Entries<AuditEntry>()
            .Any(e => e.State is EntityState.Modified or EntityState.Deleted);
        if (tampered)
        {
            throw new InvalidOperationException("Audit entries are append-only: they cannot be modified or deleted.");
        }
    }
}
