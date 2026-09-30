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

        // Projected in SQL: credentials and security data are never loaded, let alone returned.
        var rows = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.Name, u.Email, u.Role, u.Active, HasPassword = u.PasswordHash != null })
            .ToListAsync(cancellationToken);

        // Email is a required column (IdentityAccessDbContext), so it is never null here.
        return rows.Select(u => new UserSummary(u.Id, u.Name, u.Email!, u.Role, User.DeriveStatus(u.Active, u.HasPassword))).ToList();
    }
}
