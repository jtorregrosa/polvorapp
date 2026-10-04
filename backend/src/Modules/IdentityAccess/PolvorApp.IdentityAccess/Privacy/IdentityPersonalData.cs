using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using PolvorApp.AuditPrivacy.Contracts;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.IdentityAccess.Persistence;
using PolvorApp.IdentityAccess.Security;
using PolvorApp.IdentityAccess.Users;
using PolvorApp.SharedKernel.Persistence;

namespace PolvorApp.IdentityAccess.Privacy;

/// <summary>
/// The identity part of a GDPR request about a user (UC-26; add-audit-privacy, design D8): their
/// profile, never a password, key, code or token. The erasure anonymises the user instead of deleting
/// them, so audit entries and orders keep their id: the name becomes a fixed marker, the email a unique
/// address that can never be delivered, the credentials, tokens, claims and logins go, and the new
/// security stamp ends every session and remembered device.
/// </summary>
internal sealed class IdentityPersonalData(IdentityAccessDbContext db, ICurrentUser currentUser, TimeProvider time) : IPersonalDataParticipant
{
    public const string ProfileSheet = "profile";

    /// <summary>The name an erased user keeps; the UI shows "Erased user" for every erased user.</summary>
    public const string ErasedName = "—";

    public int Order => PersonalDataParticipantOrder.Identity;

    public static string ErasedEmail(Guid userId) => $"erased-{userId:N}@erased.invalid";

    public async Task<PersonalDataSummary> DescribeAsync(PersonalDataSubject subject, CancellationToken cancellationToken)
    {
        if (subject is not PersonalDataSubject.UserAccount(var userId))
        {
            return PersonalDataSummary.Empty;
        }

        var exists = await db.Users.AnyAsync(u => u.Id == userId && u.ErasedAt == null, cancellationToken);
        return exists ? new PersonalDataSummary { Counts = new Dictionary<string, int> { ["userAccount"] = 1 } } : PersonalDataSummary.Empty;
    }

    public async Task<PersonalDataExportPart> ExportAsync(PersonalDataSubject subject, CancellationToken cancellationToken)
    {
        if (subject is not PersonalDataSubject.UserAccount(var userId))
        {
            return PersonalDataExportPart.Empty;
        }

        // Projected in SQL: credentials and security data are never loaded.
        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId && u.ErasedAt == null)
            .Select(u => new
            {
                u.Name,
                u.Email,
                u.Role,
                u.Locale,
                u.Active,
                HasPassword = u.PasswordHash != null,
                u.CreatedAt,
                u.LastSignInAt,
                u.TwoFactorEnabled,
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (user is null)
        {
            return PersonalDataExportPart.Empty;
        }

        return new PersonalDataExportPart(
            [
                new PersonalDataSheet(
                    ProfileSheet,
                    ["name", "email", "role", "language", "status", "createdAt", "lastSignInAt", "twoFactorEnabled"],
                    [[
                        user.Name, user.Email, user.Role.ToCode(), user.Locale,
                        User.DeriveStatus(user.Active, user.HasPassword, erased: false).ToString().ToUpperInvariant(),
                        user.CreatedAt, user.LastSignInAt, user.TwoFactorEnabled,
                    ]]),
            ],
            []);
    }

    public async Task PrepareErasureAsync(
        PersonalDataSubject subject, PersonalDataErasure erasure, DbTransaction transaction, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(erasure);
        if (subject is not PersonalDataSubject.UserAccount(var userId))
        {
            return;
        }

        // Your own account would leave nobody to answer for the erasure.
        if (currentUser.UserId == userId)
        {
            throw new PersonalDataErasureRefusedException(PersonalDataErasureRefusedException.SelfErasure);
        }

        await db.EnlistAsync(transaction, cancellationToken);
        // The same lock order as every change that can remove an Admin (spec: last Admin is protected):
        // the advisory lock, then the user's row.
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({UserLock.AdminChangesKey})", cancellationToken);
        var user = await db.Users.FromSql($"SELECT * FROM identity.users WHERE id = {userId} FOR UPDATE")
            .AsNoTracking()
            .Select(u => new { u.NormalizedEmail, u.ErasedAt, u.Role, u.Active, HasPassword = u.PasswordHash != null })
            .SingleOrDefaultAsync(cancellationToken);
        if (user is null)
        {
            return;
        }

        if (user.ErasedAt is not null)
        {
            throw new PersonalDataErasureRefusedException(PersonalDataErasureRefusedException.AlreadyErased);
        }

        if (user is { Role: UserRole.Admin, Active: true, HasPassword: true }
            && !await db.Users.AnyAsync(
                u => u.Id != userId && u.Role == UserRole.Admin && u.Active && u.PasswordHash != null && u.ErasedAt == null, cancellationToken))
        {
            throw new PersonalDataErasureRefusedException(PersonalDataErasureRefusedException.LastAdmin);
        }

        erasure.UserFound = true;
        erasure.FormerEmail = user.NormalizedEmail;
    }

    public async Task EraseAsync(
        PersonalDataSubject subject, PersonalDataErasure erasure, DbTransaction transaction, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(erasure);
        if (subject is not PersonalDataSubject.UserAccount(var userId) || !erasure.UserFound)
        {
            return;
        }

        await db.EnlistAsync(transaction, cancellationToken);
        var email = ErasedEmail(userId);
        var normalised = email.ToUpperInvariant();
        var now = time.GetUtcNow();
        var stamp = Guid.NewGuid().ToString("N");
        var updated = await db.Database.ExecuteSqlAsync(
            $"""
            UPDATE identity.users
            SET name = {ErasedName}, email = {email}, normalized_email = {normalised}, user_name = {email},
                normalized_user_name = {normalised}, password_hash = NULL, two_factor_enabled = FALSE, active = FALSE,
                last_sign_in_at = NULL, security_stamp = {stamp}, concurrency_stamp = {stamp}, erased_at = {now}
            WHERE id = {userId}
            """,
            cancellationToken);
        if (updated != 1)
        {
            throw new InvalidOperationException("The user locked for the erasure is gone.");
        }

        await db.Database.ExecuteSqlAsync($"DELETE FROM identity.user_tokens WHERE user_id = {userId}", cancellationToken);
        await db.Database.ExecuteSqlAsync($"DELETE FROM identity.user_claims WHERE user_id = {userId}", cancellationToken);
        await db.Database.ExecuteSqlAsync($"DELETE FROM identity.user_logins WHERE user_id = {userId}", cancellationToken);
        erasure.Count("usersErased", 1);
    }
}
