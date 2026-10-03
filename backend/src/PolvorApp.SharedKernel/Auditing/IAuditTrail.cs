using Microsoft.EntityFrameworkCore;

namespace PolvorApp.SharedKernel.Auditing;

/// <summary>What a module reports about a change; the trail adds time, actor and trace id.</summary>
/// <param name="Action">Culture-independent action code, e.g. <c>UserInvited</c>.</param>
/// <param name="EntityType">Type of the changed entity, e.g. <c>User</c>.</param>
/// <param name="EntityId">Identifier of the changed entity.</param>
/// <param name="Data">Serialised to JSON; must never contain secrets (passwords, codes, tokens).</param>
/// <param name="ComparsaId">The comparsa concerned, when there is one.</param>
/// <param name="ActorUserId">Overrides the signed-in user, e.g. while that user is signing in.</param>
/// <param name="Anonymous">
/// The actor is unknown even if a session cookie is present, e.g. a failed sign-in attempt.
/// </param>
public sealed record AuditRecord(
    string Action,
    string EntityType,
    string? EntityId = null,
    object? Data = null,
    Guid? ComparsaId = null,
    Guid? ActorUserId = null,
    bool Anonymous = false);

/// <summary>Records audit entries in the caller's unit of work (spec: Audit trail of writes and security events).</summary>
public interface IAuditTrail
{
    /// <summary>
    /// Adds an entry to <paramref name="context"/>; it is stored by the caller's next
    /// <c>SaveChanges</c>, in the same transaction as the change. The context must map the audit
    /// trail with <see cref="AuditTrailModel.AddAuditTrail"/>.
    /// </summary>
    void Record(DbContext context, AuditRecord record);
}

/// <summary>
/// Records an audit entry on its own, in its own transaction, for actions that write nothing and so
/// have no unit of work to join, such as an export (change add-exports, design D2). Writes use
/// <see cref="IAuditTrail"/> instead, so the entry commits with the change.
/// </summary>
public interface IAuditLog
{
    /// <summary>Stores the entry; throws when it cannot, so the caller does not go on unaudited.</summary>
    Task RecordAsync(AuditRecord record, CancellationToken cancellationToken);
}
