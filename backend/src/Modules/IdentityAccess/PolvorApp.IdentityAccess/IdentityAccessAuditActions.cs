using PolvorApp.IdentityAccess.Security;
using PolvorApp.IdentityAccess.Users;
using PolvorApp.SharedKernel.Auditing;

namespace PolvorApp.IdentityAccess;

/// <summary>The audit action codes this module records (add-audit-privacy, design D2).</summary>
internal static class IdentityAccessAuditActions
{
    public static readonly IReadOnlyList<AuditActionDefinition> All =
    [
        new(SecurityEvents.SignedIn, SecurityEvents.EntityType, AuditRetentionClass.Security),
        new(SecurityEvents.SignInFailed, SecurityEvents.EntityType, AuditRetentionClass.Security),
        new(SecurityEvents.LockedOut, SecurityEvents.EntityType, AuditRetentionClass.Security),
        new(SecurityEvents.TwoFactorEnrolled, SecurityEvents.EntityType),
        new(SecurityEvents.RecoveryCodeUsed, SecurityEvents.EntityType, AuditRetentionClass.Security),
        new(SecurityEvents.RecoveryCodesRegenerated, SecurityEvents.EntityType),
        new(SecurityEvents.PasswordChanged, SecurityEvents.EntityType),
        new(SecurityEvents.PasswordResetRequested, SecurityEvents.EntityType, AuditRetentionClass.Security),
        new(SecurityEvents.PasswordReset, SecurityEvents.EntityType),
        new(SecurityEvents.SignedOutEverywhere, SecurityEvents.EntityType),
        new(SecurityEvents.InvitationAccepted, SecurityEvents.EntityType),
        new(SecurityEvents.UserInvited, SecurityEvents.EntityType),
        new(SecurityEvents.InvitationResent, SecurityEvents.EntityType),
        new(SecurityEvents.UserUpdated, SecurityEvents.EntityType),
        new(SecurityEvents.UserDeactivated, SecurityEvents.EntityType),
        new(SecurityEvents.UserReactivated, SecurityEvents.EntityType),
        new(SecurityEvents.TwoFactorReset, SecurityEvents.EntityType),
        new(SecurityEvents.LocaleChanged, SecurityEvents.EntityType),
        new(CreateAdminCommand.AdminBootstrapRefused, SecurityEvents.EntityType, AuditRetentionClass.Security),
    ];
}
