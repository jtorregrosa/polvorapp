using System.Text.Json.Serialization;

namespace PolvorApp.IdentityAccess.Contracts;

/// <summary>A user's single role (glossary: <c>ADMIN</c> | <c>FIRING_CHIEF</c>).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<UserRole>))]
public enum UserRole
{
    /// <summary>Comparsa officer, scoped to the comparsas assigned to them (BR-12). The default (0) is the least privileged role.</summary>
    [JsonStringEnumMemberName("FIRING_CHIEF")]
    FiringChief = 0,

    /// <summary>Federation user with full access.</summary>
    [JsonStringEnumMemberName("ADMIN")]
    Admin = 1,
}

/// <summary>Stored and claim codes of <see cref="UserRole"/>, culture-independent (ADR-0007).</summary>
public static class UserRoleCodes
{
    public const string Admin = "ADMIN";
    public const string FiringChief = "FIRING_CHIEF";

    public static string ToCode(this UserRole role) => role switch
    {
        UserRole.Admin => Admin,
        UserRole.FiringChief => FiringChief,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown role."),
    };

    public static UserRole? FromCode(string? code) => code switch
    {
        Admin => UserRole.Admin,
        FiringChief => UserRole.FiringChief,
        _ => null,
    };

    /// <summary>Parses a stored code; an unknown code is corrupt data, never a default role.</summary>
    public static UserRole Parse(string code) =>
        FromCode(code) ?? throw new FormatException("Unknown user role code.");
}

/// <summary>Authorization policy names other modules put on their endpoints.</summary>
public static class AuthorizationPolicies
{
    /// <summary>Only Admins (Federation).</summary>
    public const string Admin = "Admin";
}
