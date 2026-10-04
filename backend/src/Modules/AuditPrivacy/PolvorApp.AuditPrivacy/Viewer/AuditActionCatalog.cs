using System.Collections.Frozen;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PolvorApp.SharedKernel.Auditing;

namespace PolvorApp.AuditPrivacy.Viewer;

/// <summary>
/// Every audit action code the modules declare (design D2). The audit log validates its filters
/// against it, the retention purge reads the security codes from it, and recording an undeclared
/// action is refused. A code declared twice is a programming error found at start-up.
/// </summary>
internal sealed class AuditActionCatalog
{
    private readonly FrozenDictionary<string, AuditActionDefinition> _byCode;
    private readonly FrozenSet<string> _entityTypes;

    public AuditActionCatalog(IEnumerable<IAuditActionSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        var byCode = new Dictionary<string, AuditActionDefinition>(StringComparer.Ordinal);
        foreach (var action in sources.SelectMany(source => source.Actions))
        {
            if (!byCode.TryAdd(action.Code, action))
            {
                throw new InvalidOperationException($"The audit action '{action.Code}' is declared more than once.");
            }
        }

        _byCode = byCode.ToFrozenDictionary(StringComparer.Ordinal);
        _entityTypes = byCode.Values.Select(a => a.EntityType).ToFrozenSet(StringComparer.Ordinal);
        Actions = [.. byCode.Values.OrderBy(a => a.Code, StringComparer.Ordinal)];
        SecurityActions = [.. Actions.Where(a => a.Retention == AuditRetentionClass.Security).Select(a => a.Code)];
    }

    /// <summary>Every declared action, by code.</summary>
    public IReadOnlyList<AuditActionDefinition> Actions { get; }

    /// <summary>The codes kept for the security retention period.</summary>
    public IReadOnlyList<string> SecurityActions { get; }

    public bool Contains(string code) => _byCode.ContainsKey(code);

    /// <summary>Whether <paramref name="code"/> is declared for <paramref name="entityType"/>.</summary>
    public bool Declares(string code, string entityType) =>
        _byCode.TryGetValue(code, out var action) && string.Equals(action.EntityType, entityType, StringComparison.Ordinal);

    public bool ContainsEntityType(string entityType) => _entityTypes.Contains(entityType);

    /// <summary>The retention of <paramref name="code"/>; an undeclared code (e.g. renamed since) is kept longer.</summary>
    public AuditRetentionClass RetentionOf(string code) =>
        _byCode.TryGetValue(code, out var action) ? action.Retention : AuditRetentionClass.Standard;
}

/// <summary>
/// Checks the catalogue at start-up (design D2): building it refuses a code declared twice, and every
/// record resolver must name a declared entity type, once. A broken declaration stops the host rather
/// than its first audited request.
/// </summary>
internal sealed class AuditActionCatalogCheck(AuditActionCatalog catalog, IServiceScopeFactory scopes) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var types = scope.ServiceProvider.GetServices<IAuditRecordResolver>().Select(r => r.EntityType).ToList();
        if (types.FirstOrDefault(type => !catalog.ContainsEntityType(type)) is { } undeclared)
        {
            throw new InvalidOperationException($"A record resolver names '{undeclared}', which no audit action declares.");
        }

        if (types.GroupBy(type => type, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1) is { } duplicate)
        {
            throw new InvalidOperationException($"More than one record resolver names '{duplicate.Key}'.");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
