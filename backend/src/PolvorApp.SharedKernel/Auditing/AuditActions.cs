using Microsoft.Extensions.DependencyInjection;

namespace PolvorApp.SharedKernel.Auditing;

/// <summary>
/// How long an audit entry is kept (spec audit-privacy: Audit retention): access and security
/// events for a year, everything else for five years.
/// </summary>
public enum AuditRetentionClass
{
    Standard,
    Security,
}

/// <summary>An audit action code a module records, the entity type it is recorded under and its retention.</summary>
public sealed record AuditActionDefinition(string Code, string EntityType, AuditRetentionClass Retention = AuditRetentionClass.Standard);

/// <summary>
/// The audit action codes of one module (add-audit-privacy, design D2). The audit log validates its
/// filters against them, the retention purge reads their retention, and recording an action that no
/// module declares is a programming error.
/// </summary>
public interface IAuditActionSource
{
    IReadOnlyList<AuditActionDefinition> Actions { get; }
}

/// <summary>Registration of a module's audit action codes.</summary>
public static class AuditActionRegistration
{
    /// <summary>The longest action code or entity type the audit table stores.</summary>
    public const int MaxCodeLength = 100;

    public static IServiceCollection AddAuditActions(this IServiceCollection services, IEnumerable<AuditActionDefinition> actions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(actions);
        AuditActionDefinition[] declared = [.. actions];
        foreach (var action in declared)
        {
            // The audit trail refuses codes longer than this, so a declared code is always recordable.
            if (string.IsNullOrWhiteSpace(action.Code) || action.Code.Length > MaxCodeLength
                || string.IsNullOrWhiteSpace(action.EntityType) || action.EntityType.Length > MaxCodeLength)
            {
                throw new ArgumentException($"Audit action '{action.Code}' on '{action.EntityType}' is not a valid declaration.", nameof(actions));
            }
        }

        services.AddSingleton<IAuditActionSource>(new AuditActionSource(declared));
        return services;
    }

    private sealed class AuditActionSource(IReadOnlyList<AuditActionDefinition> actions) : IAuditActionSource
    {
        public IReadOnlyList<AuditActionDefinition> Actions { get; } = actions;
    }
}
