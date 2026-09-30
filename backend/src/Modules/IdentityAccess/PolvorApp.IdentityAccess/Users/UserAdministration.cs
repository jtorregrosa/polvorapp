using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.IdentityAccess.Emails;
using PolvorApp.IdentityAccess.Persistence;
using PolvorApp.IdentityAccess.Security;
using PolvorApp.SharedKernel.Auditing;
using PolvorApp.SharedKernel.Email;

namespace PolvorApp.IdentityAccess.Users;

/// <summary>How a user-management operation ended.</summary>
internal enum AdminOutcome
{
    Done,
    NotFound,
    EmailTaken,
    LastAdmin,
    NotInvited,
    NotEnrolled,

    /// <summary>The change is saved but the email could not be sent; the invitation can be resent.</summary>
    EmailNotSent,
}

/// <summary>
/// User management by Admins (spec: Invitation-only accounts, User management by Admins). Every
/// change is audited in its own transaction; changes that affect access rotate the security stamp
/// so they apply on the user's next request, end remembered devices and invalidate old links.
/// </summary>
internal sealed class UserAdministration(
    UserManager<User> users,
    IdentityAccessDbContext db,
    IAuditTrail trail,
    IdentityEmails emails,
    TimeProvider time)
{
    public async Task<(AdminOutcome Outcome, User? User)> InviteAsync(
        string email, string name, UserRole role, string locale, CancellationToken cancellationToken)
    {
        if (await users.FindByEmailAsync(email) is not null)
        {
            return (AdminOutcome.EmailTaken, null);
        }

        var user = new User
        {
            UserName = email,
            Email = email,
            Name = name,
            Role = role,
            Locale = locale,
            CreatedAt = time.GetUtcNow(),
        };

        string token;
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            IdentityResult created;
            try
            {
                created = await users.CreateAsync(user);
            }
            catch (DbUpdateException exception) when (exception.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
            {
                // A concurrent invitation of the same email won the race on the unique index.
                return (AdminOutcome.EmailTaken, null);
            }

            if (!created.Succeeded)
            {
                return created.Errors.Any(e => e.Code is nameof(IdentityErrorDescriber.DuplicateEmail) or nameof(IdentityErrorDescriber.DuplicateUserName))
                    ? (AdminOutcome.EmailTaken, null)
                    : throw new InvalidOperationException($"Creating the user failed: {string.Join(", ", created.Errors.Select(e => e.Code))}.");
            }

            token = await users.GenerateUserTokenAsync(user, InvitationTokenProvider.ProviderName, InvitationTokenProvider.Purpose);
            trail.Record(db, SecurityEvents.UserInvited, user, new { role = role.ToCode(), locale });
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return (await TrySendInvitationAsync(user, token, cancellationToken), user);
    }

    public async Task<(AdminOutcome Outcome, User? User)> UpdateAsync(
        Guid id, string name, UserRole role, string locale, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return (AdminOutcome.NotFound, null);
        }

        await using var transaction = await BeginAdminChangeAsync(user, cancellationToken);
        if (user.Role == UserRole.Admin && role != UserRole.Admin && await IsLastActiveAdminAsync(user, cancellationToken))
        {
            return (AdminOutcome.LastAdmin, user);
        }

        if (user.Name == name && user.Role == role && user.Locale == locale)
        {
            return (AdminOutcome.Done, user);
        }

        // The principal is rebuilt on every request (ValidationInterval = 0), so a role change
        // applies on the user's next request without ending the session (spec: Role change takes effect).
        var previous = new { name = user.Name, role = user.Role.ToCode(), locale = user.Locale };
        user.Name = name;
        user.Role = role;
        user.Locale = locale;
        trail.Record(db, SecurityEvents.UserUpdated, user, new { previous, current = new { name, role = role.ToCode(), locale } });
        await users.UpdateAsync(user).ThrowIfFailedAsync("Updating the user");
        await transaction.CommitAsync(cancellationToken);
        return (AdminOutcome.Done, user);
    }

    public async Task<(AdminOutcome Outcome, User? User)> SetActiveAsync(Guid id, bool active, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return (AdminOutcome.NotFound, null);
        }

        await using var transaction = await BeginAdminChangeAsync(user, cancellationToken);
        if (user.Active == active)
        {
            return (AdminOutcome.Done, user);
        }

        if (!active && user.Role == UserRole.Admin && await IsLastActiveAdminAsync(user, cancellationToken))
        {
            return (AdminOutcome.LastAdmin, user);
        }

        user.Active = active;
        trail.Record(db, active ? SecurityEvents.UserReactivated : SecurityEvents.UserDeactivated, user);
        await users.UpdateAsync(user).ThrowIfFailedAsync("Changing the user's status");
        // Deactivation ends sessions, remembered devices and pending invitation links.
        await users.UpdateSecurityStampAsync(user).ThrowIfFailedAsync("Ending the user's sessions");
        await transaction.CommitAsync(cancellationToken);
        return (AdminOutcome.Done, user);
    }

    public async Task<(AdminOutcome Outcome, User? User)> ResendInvitationAsync(Guid id, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return (AdminOutcome.NotFound, null);
        }

        string token;
        await using (var transaction = await db.LockAsync(user, cancellationToken))
        {
            if (user.Status != UserStatus.Invited)
            {
                return (AdminOutcome.NotInvited, user);
            }

            // A new stamp invalidates the previous link.
            await users.UpdateSecurityStampAsync(user).ThrowIfFailedAsync("Invalidating the previous invitation");
            token = await users.GenerateUserTokenAsync(user, InvitationTokenProvider.ProviderName, InvitationTokenProvider.Purpose);
            trail.Record(db, SecurityEvents.InvitationResent, user);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return (await TrySendInvitationAsync(user, token, cancellationToken), user);
    }

    public async Task<(AdminOutcome Outcome, User? User)> ResetTwoFactorAsync(Guid id, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return (AdminOutcome.NotFound, null);
        }

        await using var transaction = await db.LockAsync(user, cancellationToken);
        if (!user.TwoFactorEnabled)
        {
            return (AdminOutcome.NotEnrolled, user);
        }

        await users.SetTwoFactorEnabledAsync(user, false).ThrowIfFailedAsync("Disabling two-factor authentication");
        await users.RemoveAuthenticationTokenAsync(user, IdentityTokens.StoreProvider, IdentityTokens.AuthenticatorKey).ThrowIfFailedAsync("Removing the authenticator key");
        await users.RemoveAuthenticationTokenAsync(user, IdentityTokens.StoreProvider, IdentityTokens.RecoveryCodes).ThrowIfFailedAsync("Removing the recovery codes");
        await ReplaySafeAuthenticatorTokenProvider.ForgetAsync(users, user).ThrowIfFailedAsync("Forgetting the last code");
        trail.Record(db, SecurityEvents.TwoFactorReset, user);
        await users.UpdateSecurityStampAsync(user).ThrowIfFailedAsync("Ending the user's sessions");
        await transaction.CommitAsync(cancellationToken);
        return (AdminOutcome.Done, user);
    }

    /// <summary>
    /// Serialises every change that can remove an active Admin (spec: last Admin is protected):
    /// a transaction-scoped advisory lock, then the target's row, reloaded.
    /// </summary>
    private Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction> BeginAdminChangeAsync(User user, CancellationToken cancellationToken) =>
        db.LockAdminsAsync(user, cancellationToken);

    /// <summary>Whether <paramref name="user"/> is an active Admin and no other active Admin exists.</summary>
    private async Task<bool> IsLastActiveAdminAsync(User user, CancellationToken cancellationToken) =>
        user is { Role: UserRole.Admin, Active: true, PasswordHash: not null }
        && !await db.Users.AnyAsync(
            u => u.Id != user.Id && u.Role == UserRole.Admin && u.Active && u.PasswordHash != null, cancellationToken);

    private async Task<AdminOutcome> TrySendInvitationAsync(User user, string token, CancellationToken cancellationToken)
    {
        try
        {
            await emails.SendInvitationAsync(user, token, cancellationToken);
            return AdminOutcome.Done;
        }
        catch (EmailDeliveryException)
        {
            // Logged by the sender (template and error code only); the Admin is told and can resend.
            return AdminOutcome.EmailNotSent;
        }
    }
}
