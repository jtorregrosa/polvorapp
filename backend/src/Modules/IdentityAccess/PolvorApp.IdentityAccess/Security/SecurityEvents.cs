using PolvorApp.IdentityAccess.Persistence;
using PolvorApp.IdentityAccess.Users;
using PolvorApp.SharedKernel.Auditing;

namespace PolvorApp.IdentityAccess.Security;

/// <summary>
/// Audit action codes of identity-access (spec: Security events are audited). Entries are added to
/// the identity context and commit with the next save of the flow (design D3).
/// </summary>
internal static class SecurityEvents
{
    public const string SignedIn = "SignedIn";
    public const string SignInFailed = "SignInFailed";
    public const string LockedOut = "LockedOut";
    public const string TwoFactorEnrolled = "TwoFactorEnrolled";
    public const string RecoveryCodeUsed = "RecoveryCodeUsed";
    public const string RecoveryCodesRegenerated = "RecoveryCodesRegenerated";
    public const string PasswordChanged = "PasswordChanged";
    public const string PasswordResetRequested = "PasswordResetRequested";
    public const string PasswordReset = "PasswordReset";
    public const string SignedOutEverywhere = "SignedOutEverywhere";
    public const string InvitationAccepted = "InvitationAccepted";
    public const string UserInvited = "UserInvited";
    public const string InvitationResent = "InvitationResent";
    public const string UserUpdated = "UserUpdated";
    public const string UserDeactivated = "UserDeactivated";
    public const string UserReactivated = "UserReactivated";
    public const string TwoFactorReset = "TwoFactorReset";
    public const string LocaleChanged = "LocaleChanged";

    public const string EntityType = "User";

    /// <summary>Records an event about <paramref name="user"/> done by the signed-in user (e.g. an Admin).</summary>
    public static void Record(this IAuditTrail trail, IdentityAccessDbContext db, string action, User user, object? data = null) =>
        trail.Record(db, new AuditRecord(action, EntityType, user.Id.ToString(), data));

    /// <summary>Records an event the user did to their own account (sign-in, enrolment, password change).</summary>
    public static void RecordBySelf(this IAuditTrail trail, IdentityAccessDbContext db, string action, User user, object? data = null) =>
        trail.Record(db, new AuditRecord(action, EntityType, user.Id.ToString(), data, ActorUserId: user.Id));

    /// <summary>An attempt by someone not yet authenticated (whatever cookie the browser carries).</summary>
    public static void RecordAnonymous(this IAuditTrail trail, IdentityAccessDbContext db, string action, User user, object? data = null) =>
        trail.Record(db, new AuditRecord(action, EntityType, user.Id.ToString(), data, Anonymous: true));

    /// <summary>A failed attempt with no identified user: only the normalised attempted email is kept.</summary>
    public static void RecordFailedAttempt(this IAuditTrail trail, IdentityAccessDbContext db, string attemptedEmail, string step) =>
        trail.Record(db, new AuditRecord(SignInFailed, EntityType, null, new { attemptedEmail, step }, Anonymous: true));
}
