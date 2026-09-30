using System.Text.Json.Serialization;

namespace PolvorApp.IdentityAccess.Contracts;

/// <summary>Derived user status (glossary: <c>INVITED</c> | <c>ACTIVE</c> | <c>DEACTIVATED</c>).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<UserStatus>))]
public enum UserStatus
{
    [JsonStringEnumMemberName("INVITED")]
    Invited,

    [JsonStringEnumMemberName("ACTIVE")]
    Active,

    [JsonStringEnumMemberName("DEACTIVATED")]
    Deactivated,
}
