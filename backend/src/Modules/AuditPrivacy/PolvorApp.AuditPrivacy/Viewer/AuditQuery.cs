using Microsoft.EntityFrameworkCore;
using PolvorApp.AuditPrivacy.Persistence;
using PolvorApp.SharedKernel.Auditing;

namespace PolvorApp.AuditPrivacy.Viewer;

/// <summary>One page of the audit log and the cursor of the next one, if any.</summary>
internal sealed record AuditPage(IReadOnlyList<AuditEntry> Entries, AuditCursor? Next);

/// <summary>
/// Reads the audit log newest first with keyset paging on <c>(occurred_at, id)</c> (spec: Audit log
/// query (UC-25); design D10): a page continues right after the previous one's last entry, so entries
/// written meanwhile neither repeat nor shift it.
/// </summary>
internal sealed class AuditQuery(AuditDbContext db)
{
    public async Task<AuditPage> ReadAsync(AuditFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var query = db.Set<AuditEntry>().AsNoTracking();
        if (filter.From is { } from)
        {
            query = query.Where(e => e.OccurredAt >= from);
        }

        if (filter.Before is { } before)
        {
            query = query.Where(e => e.OccurredAt < before);
        }

        switch (filter.Actor)
        {
            case AuditActorFilter.NoActor:
                query = query.Where(e => e.ActorUserId == null);
                break;
            case AuditActorFilter.User(var actor):
                query = query.Where(e => e.ActorUserId == actor);
                break;
        }

        if (filter.ComparsaId is { } comparsa)
        {
            query = query.Where(e => e.ComparsaId == comparsa);
        }

        if (filter.Entity is { } entity)
        {
            var entityType = entity.Type;
            query = query.Where(e => e.EntityType == entityType);
            if (entity.Id is { } entityId)
            {
                query = query.Where(e => e.EntityId == entityId);
            }
        }

        if (filter.Action is { } action)
        {
            query = query.Where(e => e.Action == action);
        }

        if (filter.Cursor is { } cursor)
        {
            var at = cursor.OccurredAt;
            var id = cursor.Id;
            // The plain bound lets the filtered (…, occurred_at, id) indexes use the range; the row
            // comparison breaks ties between entries of the same instant.
            query = query.Where(e => e.OccurredAt <= at
                && EF.Functions.LessThan(ValueTuple.Create(e.OccurredAt, e.Id), ValueTuple.Create(at, id)));
        }

        var rows = await query
            .OrderByDescending(e => e.OccurredAt)
            .ThenByDescending(e => e.Id)
            .Take(filter.Limit + 1)
            .ToListAsync(cancellationToken);
        if (rows.Count <= filter.Limit)
        {
            return new AuditPage(rows, null);
        }

        var last = rows[filter.Limit - 1];
        return new AuditPage(rows[..filter.Limit], new AuditCursor(last.OccurredAt, last.Id));
    }
}
