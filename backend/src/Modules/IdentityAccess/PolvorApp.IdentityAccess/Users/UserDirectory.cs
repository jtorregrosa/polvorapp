using Microsoft.EntityFrameworkCore;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.IdentityAccess.Persistence;

namespace PolvorApp.IdentityAccess.Users;

/// <summary>Read-only <see cref="IUserDirectory"/> over the identity schema (design D2 of add-federation-catalog).</summary>
internal sealed class UserDirectory(IdentityAccessDbContext db) : IUserDirectory
{
    public async Task<UserSummary?> FindAsync(Guid userId, CancellationToken cancellationToken) =>
        (await FindManyAsync([userId], cancellationToken)).SingleOrDefault();

    public async Task<IReadOnlyList<UserSummary>> FindManyAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(userIds);
        if (userIds.Count == 0)
        {
            return [];
        }

        return await ProjectAsync(db.Users.Where(u => userIds.Contains(u.Id)), cancellationToken);
    }

    public Task<IReadOnlyList<UserSummary>> ListAsync(UserRole role, CancellationToken cancellationToken) =>
        ProjectAsync(db.Users.Where(u => u.Role == role), cancellationToken);

    private static async Task<IReadOnlyList<UserSummary>> ProjectAsync(IQueryable<User> users, CancellationToken cancellationToken)
    {
        // Projected in SQL: credentials and security data are never loaded, let alone returned.
        var rows = await users.AsNoTracking()
            .Select(u => new { u.Id, u.Name, u.Email, u.Role, u.Active, HasPassword = u.PasswordHash != null, u.Locale })
            .ToListAsync(cancellationToken);

        // Email is a required column (IdentityAccessDbContext), so it is never null here.
        return rows.Select(u => new UserSummary(u.Id, u.Name, u.Email!, u.Role, User.DeriveStatus(u.Active, u.HasPassword), u.Locale)).ToList();
    }
}
