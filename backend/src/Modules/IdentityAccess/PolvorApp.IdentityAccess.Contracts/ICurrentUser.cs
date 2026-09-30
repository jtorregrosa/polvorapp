namespace PolvorApp.IdentityAccess.Contracts;

/// <summary>The signed-in user of the current request, as the server sees it.</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    /// <summary>Null when not signed in.</summary>
    Guid? UserId { get; }

    /// <summary>Null when not signed in.</summary>
    UserRole? Role { get; }

    bool IsAdmin => Role == UserRole.Admin;
}
