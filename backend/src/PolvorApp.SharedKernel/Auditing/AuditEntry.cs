namespace PolvorApp.SharedKernel.Auditing;

/// <summary>
/// One immutable row of the audit trail (SEC-05, UC-25). The audit-privacy module owns the table;
/// every module maps it too, so an entry commits in the same transaction as the change it
/// describes (change add-identity-access, design D3). Never holds passwords, codes or tokens.
/// Create entries only through <see cref="IAuditTrail"/>, which fills time, actor and trace id.
/// </summary>
public sealed class AuditEntry
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>The acting user; null for anonymous events such as a failed sign-in.</summary>
    public Guid? ActorUserId { get; init; }

    /// <summary>Culture-independent action code, e.g. <c>UserInvited</c>.</summary>
    public required string Action { get; init; }

    public required string EntityType { get; init; }

    public string? EntityId { get; init; }

    /// <summary>
    /// The comparsa the change concerns, when there is one (BR-12 reporting). Like the actor, no
    /// foreign key: the entry crosses module boundaries and outlives users and comparsas.
    /// </summary>
    public Guid? ComparsaId { get; init; }

    /// <summary>Correlation identifier of the request that made the change (NFR-12).</summary>
    public string? TraceId { get; init; }

    /// <summary>Structured description of the change as JSON (previous and new values).</summary>
    public string? Data { get; init; }
}
