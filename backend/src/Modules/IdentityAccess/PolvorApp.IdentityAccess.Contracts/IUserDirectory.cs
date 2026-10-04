namespace PolvorApp.IdentityAccess.Contracts;

/// <summary>A user as other modules see them: never credentials or security data.</summary>
/// <param name="Id">User identifier.</param>
/// <param name="Name">Display name.</param>
/// <param name="Email">Sign-in email.</param>
/// <param name="Role">Role.</param>
/// <param name="Status">Derived status.</param>
/// <param name="Locale">Preferred language (<c>es-ES</c>, <c>ca-ES-valencia</c> or <c>en</c>), used for emails.</param>
public sealed record UserSummary(Guid Id, string Name, string Email, UserRole Role, UserStatus Status, string Locale);

/// <summary>
/// Read-only lookup of users for other modules, e.g. to validate and show FiringChief
/// assignments (change add-federation-catalog, design D2). Deactivated users are included, so
/// callers can reject them. Names and emails are personal data: callers enforce their own
/// authorisation (Admin-only or BR-12 scope) before returning them.
/// </summary>
public interface IUserDirectory
{
    /// <summary>The user, or null when no user has that id.</summary>
    Task<UserSummary?> FindAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// The users that exist among <paramref name="userIds"/>, each once and in no particular order;
    /// unknown ids are skipped.
    /// </summary>
    Task<IReadOnlyList<UserSummary>> FindManyAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken);

    /// <summary>
    /// Every user with <paramref name="role"/>, in any status and in no particular order, e.g. the
    /// recipients of a notification (change add-notifications, design D6). Callers filter by status.
    /// </summary>
    Task<IReadOnlyList<UserSummary>> ListAsync(UserRole role, CancellationToken cancellationToken);
}
