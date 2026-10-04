using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace PolvorApp.SharedKernel.Auditing;

/// <summary>
/// Tells the audit log which records of one entity type still exist, so an entry links to its record
/// only while there is one (add-audit-privacy, design D10). Registered by the module that owns the type.
/// </summary>
public interface IAuditRecordResolver
{
    string EntityType { get; }

    /// <summary>The ids among <paramref name="entityIds"/> whose record exists; ids that are not GUIDs never do.</summary>
    Task<IReadOnlySet<string>> ExistingAsync(IReadOnlyCollection<string> entityIds, CancellationToken cancellationToken);
}

/// <summary>Registration of record resolvers for entities keyed by a GUID <c>Id</c>.</summary>
public static class AuditRecordResolvers
{
    public static IServiceCollection AddAuditRecordResolver<TContext, TEntity>(this IServiceCollection services, string entityType)
        where TContext : DbContext
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityType);
        services.AddScoped<IAuditRecordResolver>(provider =>
            new GuidKeyedRecordResolver<TContext, TEntity>(provider.GetRequiredService<TContext>(), entityType));
        return services;
    }

    private sealed class GuidKeyedRecordResolver<TContext, TEntity>(TContext db, string entityType) : IAuditRecordResolver
        where TContext : DbContext
        where TEntity : class
    {
        public string EntityType => entityType;

        public async Task<IReadOnlySet<string>> ExistingAsync(IReadOnlyCollection<string> entityIds, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(entityIds);
            var ids = entityIds
                .Select(id => Guid.TryParse(id, out var guid) ? guid : (Guid?)null)
                .OfType<Guid>()
                .Distinct()
                .ToList();
            if (ids.Count == 0)
            {
                return new HashSet<string>();
            }

            var existing = await db.Set<TEntity>().AsNoTracking()
                .Where(e => ids.Contains(EF.Property<Guid>(e, "Id")))
                .Select(e => EF.Property<Guid>(e, "Id"))
                .ToListAsync(cancellationToken);
            return existing.Select(id => id.ToString()).ToHashSet(StringComparer.Ordinal);
        }
    }
}
