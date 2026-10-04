using Microsoft.EntityFrameworkCore;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.Notifications.Contracts;
using PolvorApp.Notifications.Persistence;
using PolvorApp.Notifications.Preferences;

namespace PolvorApp.Notifications.Sending;

/// <summary>
/// The users, their FiringChief assignments and their opt-outs at one moment (design D6), read once per
/// run from the identity and catalogue contracts. It answers who may receive what: only <c>ACTIVE</c>
/// users, only kinds of their role that they did not turn off, and FiringChiefs only for the comparsas
/// assigned to them (BR-12).
/// </summary>
internal sealed class Recipients
{
    private readonly Dictionary<Guid, UserSummary> _users;
    private readonly ILookup<Guid, Guid> _comparsasByUser;
    private readonly HashSet<(Guid UserId, NotificationKind Kind)> _optOuts;

    private Recipients(IEnumerable<UserSummary> users, IEnumerable<FiringChiefAssignmentSummary> assignments, IEnumerable<(Guid, NotificationKind)> optOuts)
    {
        _users = users.ToDictionary(u => u.Id);
        _comparsasByUser = assignments.ToLookup(a => a.UserId, a => a.ComparsaId);
        _optOuts = optOuts.ToHashSet();
    }

    /// <summary>Whether the user exists, is active, has a role the kind applies to and did not turn it off.</summary>
    public bool Wants(Guid userId, NotificationKind kind) =>
        _users.TryGetValue(userId, out var user)
        && user.Status == UserStatus.Active
        && NotificationKinds.AppliesTo(kind, user.Role)
        && !_optOuts.Contains((userId, kind));

    public UserSummary? Find(Guid userId) => _users.GetValueOrDefault(userId);

    /// <summary>The comparsas assigned to a FiringChief (active comparsas only); none for an Admin, whose assignments have no effect.</summary>
    public IReadOnlySet<Guid> ComparsasOf(Guid userId) =>
        _users.TryGetValue(userId, out var user) && user.Role == UserRole.FiringChief ? _comparsasByUser[userId].ToHashSet() : [];

    /// <summary>Every user of <paramref name="role"/> who wants <paramref name="kind"/>.</summary>
    public IEnumerable<UserSummary> WithRole(UserRole role, NotificationKind kind) =>
        _users.Values.Where(u => u.Role == role && Wants(u.Id, kind));

    /// <summary>The FiringChiefs who want <paramref name="kind"/> and have at least one active comparsa.</summary>
    public IEnumerable<UserSummary> FiringChiefsWithComparsas(NotificationKind kind) =>
        WithRole(UserRole.FiringChief, kind).Where(u => _comparsasByUser[u.Id].Any());

    /// <summary>The FiringChiefs of <paramref name="comparsaId"/> who want <paramref name="kind"/>.</summary>
    public IEnumerable<UserSummary> FiringChiefsOf(Guid comparsaId, NotificationKind kind) =>
        WithRole(UserRole.FiringChief, kind).Where(u => _comparsasByUser[u.Id].Contains(comparsaId));

    /// <summary>The active comparsas with at least one FiringChief assigned.</summary>
    public IEnumerable<Guid> AssignedComparsas() => _comparsasByUser.SelectMany(g => g).Distinct();

    /// <summary>Reads the users of both roles, the assignments and the opt-outs.</summary>
    public static async Task<Recipients> LoadAsync(
        IUserDirectory users, ICatalogDirectory catalog, NotificationsDbContext db, CancellationToken cancellationToken)
    {
        var chiefs = await users.ListAsync(UserRole.FiringChief, cancellationToken);
        var admins = await users.ListAsync(UserRole.Admin, cancellationToken);
        var assignments = await catalog.ListFiringChiefAssignmentsAsync(cancellationToken);
        var optOuts = await db.OptOuts.AsNoTracking().Select(o => new { o.UserId, o.Kind }).ToListAsync(cancellationToken);
        return new Recipients([.. chiefs, .. admins], assignments, optOuts.Select(o => (o.UserId, o.Kind)));
    }
}
