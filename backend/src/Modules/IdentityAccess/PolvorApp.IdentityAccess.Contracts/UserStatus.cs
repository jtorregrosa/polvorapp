using System.Text.Json.Serialization;

namespace PolvorApp.IdentityAccess.Contracts;

/// <summary>Derived user status (glossary: <c>INVITED</c> | <c>ACTIVE</c> | <c>DEACTIVATED</c> | <c>ERASED</c>).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<UserStatus>))]
public enum UserStatus
{
    [JsonStringEnumMemberName("INVITED")]
    Invited,

    [JsonStringEnumMemberName("ACTIVE")]
    Active,

    [JsonStringEnumMemberName("DEACTIVATED")]
    Deactivated,

    /// <summary>The user's data was erased on a GDPR request (add-audit-privacy); never active again.</summary>
    [JsonStringEnumMemberName("ERASED")]
    Erased,
}
