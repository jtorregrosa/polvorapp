using PolvorApp.IdentityAccess.Contracts;

namespace PolvorApp.FederationCatalog.Endpoints;

/// <summary>A FiringChief of a comparsa, as Admins see it (spec: Managing assignments from the comparsa and from the user).</summary>
/// <param name="UserId">User identifier.</param>
/// <param name="Name">Display name.</param>
/// <param name="Email">Sign-in email.</param>
/// <param name="Status">Derived user status.</param>
internal sealed record FiringChiefResponse(Guid UserId, string Name, string Email, UserStatus Status);
