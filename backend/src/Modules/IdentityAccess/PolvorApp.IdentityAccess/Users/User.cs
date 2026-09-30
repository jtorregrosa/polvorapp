using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Identity;
using PolvorApp.IdentityAccess.Contracts;

namespace PolvorApp.IdentityAccess.Users;

/// <summary>
/// A PolvorApp user (data-model: User). The Identity user name is the email; users are never
/// deleted, only deactivated, so audit entries keep their actor (spec: Users and roles).
/// </summary>
internal sealed class User : IdentityUser<Guid>
{
    public const int NameMaxLength = 200;

    public required string Name { get; set; }

    public required UserRole Role { get; set; }

    /// <summary>Preferred UI and email language (<c>es-ES</c>, <c>ca-ES-valencia</c> or <c>en</c>).</summary>
    public required string Locale { get; set; }

    public bool Active { get; set; } = true;

    public required DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LastSignInAt { get; set; }

    /// <summary>Derived, never stored (spec: Users and roles).</summary>
    public UserStatus Status => !Active ? UserStatus.Deactivated : PasswordHash is null ? UserStatus.Invited : UserStatus.Active;
}

/// <summary>Derived user status (glossary: <c>INVITED</c> | <c>ACTIVE</c> | <c>DEACTIVATED</c>).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<UserStatus>))]
internal enum UserStatus
{
    [JsonStringEnumMemberName("INVITED")]
    Invited,

    [JsonStringEnumMemberName("ACTIVE")]
    Active,

    [JsonStringEnumMemberName("DEACTIVATED")]
    Deactivated,
}
