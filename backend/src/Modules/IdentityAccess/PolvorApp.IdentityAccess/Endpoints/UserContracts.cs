using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.IdentityAccess.Users;

namespace PolvorApp.IdentityAccess.Endpoints;

/// <summary>A user as Admins see it (spec: User management by Admins).</summary>
/// <param name="Id">User identifier.</param>
/// <param name="Name">Display name.</param>
/// <param name="Email">Sign-in email.</param>
/// <param name="Role">Role.</param>
/// <param name="Locale">Preferred language.</param>
/// <param name="Status">Derived status.</param>
/// <param name="TwoFactorEnabled">Whether an authenticator is enrolled.</param>
/// <param name="LastSignInAt">Last completed sign-in, if any.</param>
/// <param name="CreatedAt">When the user was invited.</param>
internal sealed record UserResponse(
    Guid Id, string Name, string Email, UserRole Role, string Locale, UserStatus Status, bool TwoFactorEnabled,
    DateTimeOffset? LastSignInAt, DateTimeOffset CreatedAt)
{
    public static UserResponse From(User user) =>
        new(user.Id, user.Name, user.Email ?? string.Empty, user.Role, user.Locale, user.Status, user.TwoFactorEnabled, user.LastSignInAt, user.CreatedAt);
}

/// <summary>Role and locale arrive as text so an invalid value is reported by field name.</summary>
/// <param name="Email">Invitee's email.</param>
/// <param name="Name">Invitee's name.</param>
/// <param name="Role"><c>ADMIN</c> or <c>FIRING_CHIEF</c>.</param>
/// <param name="Locale"><c>es-ES</c>, <c>ca-ES-valencia</c> or <c>en</c>.</param>
internal sealed record InviteUserRequest(string? Email, string? Name, string? Role, string? Locale);

/// <param name="Name">New display name.</param>
/// <param name="Role"><c>ADMIN</c> or <c>FIRING_CHIEF</c>.</param>
/// <param name="Locale"><c>es-ES</c>, <c>ca-ES-valencia</c> or <c>en</c>.</param>
internal sealed record UpdateUserRequest(string? Name, string? Role, string? Locale);

/// <param name="Name">The invitee's name.</param>
/// <param name="Email">The invitee's email.</param>
internal sealed record InvitationResponse(string Name, string Email);

/// <param name="UserId">The user from the invitation link.</param>
/// <param name="Token">The single-use token from the invitation link.</param>
/// <param name="Password">The password the invitee chose.</param>
internal sealed record AcceptInvitationRequest(Guid UserId, string? Token, string? Password);

/// <param name="Locale"><c>es-ES</c>, <c>ca-ES-valencia</c> or <c>en</c>.</param>
internal sealed record UpdateLocaleRequest(string? Locale);

/// <param name="CurrentPassword">The password in use.</param>
/// <param name="NewPassword">The new password.</param>
internal sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);

/// <param name="Code">A current authenticator code.</param>
internal sealed record RegenerateRecoveryCodesRequest(string? Code);
