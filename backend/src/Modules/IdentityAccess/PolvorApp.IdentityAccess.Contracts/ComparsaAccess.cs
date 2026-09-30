using System.Collections.Frozen;
using System.Linq.Expressions;

namespace PolvorApp.IdentityAccess.Contracts;

/// <summary>The comparsas a user may see and edit: every one for an Admin, their own for a FiringChief.</summary>
public sealed class ComparsaAccess
{
    public static readonly ComparsaAccess None = new(isAll: false, FrozenSet<Guid>.Empty);
    public static readonly ComparsaAccess All = new(isAll: true, FrozenSet<Guid>.Empty);

    private ComparsaAccess(bool isAll, IReadOnlySet<Guid> comparsaIds)
    {
        IsAll = isAll;
        ComparsaIds = comparsaIds;
    }

    public bool IsAll { get; }

    /// <summary>The assigned comparsas; empty when <see cref="IsAll"/>.</summary>
    public IReadOnlySet<Guid> ComparsaIds { get; }

    public static ComparsaAccess Only(IEnumerable<Guid> comparsaIds)
    {
        var ids = comparsaIds.ToFrozenSet();
        return ids.Count == 0 ? None : new ComparsaAccess(isAll: false, ids);
    }

    public bool CanAccess(Guid comparsaId) => IsAll || ComparsaIds.Contains(comparsaId);

    /// <summary>Restricts a query to the accessible comparsas; answer out-of-scope requests with 404.</summary>
    public IQueryable<T> Filter<T>(IQueryable<T> query, Expression<Func<T, Guid>> comparsaId)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(comparsaId);
        if (IsAll)
        {
            return query;
        }

        // A captured member (not a constant) so EF Core sends the ids as one array parameter.
        var ids = new AccessibleIds(ComparsaIds.ToArray());
        var parameter = comparsaId.Parameters[0];
        var contains = Expression.Call(
            typeof(Enumerable), nameof(Enumerable.Contains), [typeof(Guid)],
            Expression.Field(Expression.Constant(ids), nameof(AccessibleIds.Values)), comparsaId.Body);
        return query.Where(Expression.Lambda<Func<T, bool>>(contains, parameter));
    }

    private sealed class AccessibleIds(Guid[] values)
    {
#pragma warning disable CA1051, SA1401 // A field, so the expression tree can reference it.
        public readonly Guid[] Values = values;
#pragma warning restore CA1051, SA1401
    }
}
