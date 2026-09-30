using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore.Storage;
using PolvorApp.IdentityAccess.Persistence;
using PolvorApp.IdentityAccess.Users;
using PolvorApp.SharedKernel.Auditing;

namespace PolvorApp.IdentityAccess.Security;

/// <summary>How a sign-in step ended.</summary>
internal enum SignInOutcome
{
    Done,
    SecondFactor,
    Enrol,
    InvalidCredentials,
    InvalidCode,
    LockedOut,
    StepExpired,
}

/// <summary>
/// The sign-in state machine of design D5 (hardening D6b). Every attempt on a known user runs in
/// one transaction that locks the user's row (<see cref="UserLock"/>), so failure counters, the
/// TOTP replay marker, recovery codes and audit entries are consistent under parallel requests
/// and commit together. Cookies are issued only after the commit, and security writes ignore the
/// client's cancellation, so an aborted request cannot discard a counted failure.
/// </summary>
internal sealed class SignInFlow(
    UserManager<User> users,
    PolvorAppSignInManager signIn,
    IdentityAccessDbContext db,
    IAuditTrail trail,
    TimeProvider time)
{
    public const int RecoveryCodeCount = 10;
    public const int MaxEmailLength = 320;
    public const int MaxPasswordLength = 256;
    public const int MaxCodeLength = 64;

    /// <summary>Security writes complete even if the client disconnects.</summary>
    private static readonly CancellationToken Persist = CancellationToken.None;

    private static readonly Claim MultiFactor = new("amr", "mfa");

    public async Task<SignInOutcome> PasswordAsync(string? email, string? password, CancellationToken cancellationToken)
    {
        email = (email ?? string.Empty).Trim();
        password ??= string.Empty;
        var user = email.Length is > 0 and <= MaxEmailLength && password.Length <= MaxPasswordLength
            ? await users.FindByEmailAsync(email)
            : null;
        if (user is null)
        {
            VerifyDummyPassword(password);
            trail.RecordFailedAttempt(db, users.NormalizeEmail(email) ?? string.Empty, "password");
            await db.SaveChangesAsync(Persist);
            return SignInOutcome.InvalidCredentials;
        }

        if (await users.IsLockedOutAsync(user))
        {
            // Answered without queueing on the row lock, so a flood on one locked account cannot
            // hold database connections; hashed anyway so timing reveals nothing.
            VerifyDummyPassword(password);
            trail.RecordAnonymous(db, SecurityEvents.SignInFailed, user, new { step = "password" });
            await db.SaveChangesAsync(Persist);
            return SignInOutcome.InvalidCredentials;
        }

        await using var transaction = await db.LockAsync(user, Persist);
        if (user is not { Active: true, PasswordHash: not null } || await users.IsLockedOutAsync(user))
        {
            VerifyDummyPassword(password);
            trail.RecordAnonymous(db, SecurityEvents.SignInFailed, user, new { step = "password" });
            await CommitAsync(transaction);
            return SignInOutcome.InvalidCredentials;
        }

        var result = await signIn.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            trail.RecordAnonymous(db, result.IsLockedOut ? SecurityEvents.LockedOut : SecurityEvents.SignInFailed, user, new { step = "password" });
            await CommitAsync(transaction);
            return SignInOutcome.InvalidCredentials;
        }

        if (!user.TwoFactorEnabled)
        {
            await transaction.CommitAsync(Persist);
            await signIn.SignInPendingSecondStepAsync(user);
            return SignInOutcome.Enrol;
        }

        if (await signIn.IsTwoFactorClientRememberedAsync(user))
        {
            // Identity keeps the failure count for 2FA users after a correct password; this is a full sign-in.
            await users.ResetAccessFailedCountAsync(user).ThrowIfFailedAsync("Resetting failed attempts");
            await RecordSignInAsync(user, new { method = "rememberedDevice" });
            await transaction.CommitAsync(Persist);
            await signIn.SignInWithClaimsAsync(user, isPersistent: false, [MultiFactor]);
            return SignInOutcome.Done;
        }

        await transaction.CommitAsync(Persist);
        await signIn.SignInPendingSecondStepAsync(user);
        return SignInOutcome.SecondFactor;
    }

    public async Task<SignInOutcome> AuthenticatorCodeAsync(string? code, bool rememberDevice, CancellationToken cancellationToken)
    {
        var user = await signIn.GetTwoFactorAuthenticationUserAsync();
        if (user is null)
        {
            return SignInOutcome.StepExpired;
        }

        await using var transaction = await db.LockAsync(user, Persist);
        if (user is not { Active: true, TwoFactorEnabled: true })
        {
            return SignInOutcome.StepExpired;
        }

        if (await users.IsLockedOutAsync(user))
        {
            return await RejectCodeAsync(transaction, user, SignInOutcome.LockedOut, "secondFactor", newlyLocked: false);
        }

        if (!await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, Truncate(code)))
        {
            return await CountFailureAsync(transaction, user, "secondFactor");
        }

        await CompleteSecondStepAsync(transaction, user, rememberDevice, new { method = "authenticator", rememberDevice });
        return SignInOutcome.Done;
    }

    public async Task<(SignInOutcome Outcome, int CodesLeft)> RecoveryCodeAsync(string? code, CancellationToken cancellationToken)
    {
        var user = await signIn.GetTwoFactorAuthenticationUserAsync();
        if (user is null)
        {
            return (SignInOutcome.StepExpired, 0);
        }

        await using var transaction = await db.LockAsync(user, Persist);
        if (user is not { Active: true, TwoFactorEnabled: true })
        {
            return (SignInOutcome.StepExpired, 0);
        }

        if (await users.IsLockedOutAsync(user))
        {
            return (await RejectCodeAsync(transaction, user, SignInOutcome.LockedOut, "recoveryCode", newlyLocked: false), 0);
        }

        // Identity's recovery-code sign-in neither checks lockout nor counts failures (design D1
        // findings), so the code is redeemed here and failures are counted like any other.
        var redeemed = await users.RedeemTwoFactorRecoveryCodeAsync(user, Truncate(code).Trim());
        if (!redeemed.Succeeded)
        {
            return (await CountFailureAsync(transaction, user, "recoveryCode"), 0);
        }

        var left = await users.CountRecoveryCodesAsync(user);
        trail.RecordBySelf(db, SecurityEvents.RecoveryCodeUsed, user, new { codesLeft = left });
        await CompleteSecondStepAsync(transaction, user, rememberDevice: false, new { method = "recoveryCode" });
        return (SignInOutcome.Done, left);
    }

    /// <summary>The authenticator key to enrol, created once; null when no enrolment is pending.</summary>
    public async Task<(string Key, string Email)?> EnrolmentKeyAsync(CancellationToken cancellationToken)
    {
        var user = await PendingEnrolmentAsync();
        if (user is null)
        {
            return null;
        }

        await using var transaction = await db.LockAsync(user, Persist);
        if (!IsPendingEnrolment(user))
        {
            return null;
        }

        var email = user.Email ?? throw new InvalidOperationException("The user has no email address.");
        var key = await users.GetAuthenticatorKeyAsync(user);
        if (!string.IsNullOrEmpty(key))
        {
            await transaction.CommitAsync(Persist);
            return (key, email);
        }

        await users.ResetAuthenticatorKeyAsync(user).ThrowIfFailedAsync("Creating the authenticator key");
        key = await users.GetAuthenticatorKeyAsync(user)
            ?? throw new InvalidOperationException("The authenticator key was not stored.");
        await transaction.CommitAsync(Persist);
        // Creating the key rotated the stamp: re-issue the pending step with the new one.
        await signIn.SignInPendingSecondStepAsync(user);
        return (key, email);
    }

    public async Task<(SignInOutcome Outcome, IReadOnlyList<string> RecoveryCodes)> ConfirmEnrolmentAsync(string? code, CancellationToken cancellationToken)
    {
        var user = await PendingEnrolmentAsync();
        if (user is null)
        {
            return (SignInOutcome.StepExpired, []);
        }

        await using var transaction = await db.LockAsync(user, Persist);
        if (!IsPendingEnrolment(user))
        {
            return (SignInOutcome.StepExpired, []);
        }

        if (await users.IsLockedOutAsync(user))
        {
            return (await RejectCodeAsync(transaction, user, SignInOutcome.LockedOut, "enrolment", newlyLocked: false), []);
        }

        if (!await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, Truncate(code)))
        {
            return (await CountFailureAsync(transaction, user, "enrolment"), []);
        }

        await users.SetTwoFactorEnabledAsync(user, true).ThrowIfFailedAsync("Enabling two-factor authentication");
        var codes = (await users.GenerateNewTwoFactorRecoveryCodesAsync(user, RecoveryCodeCount))?.ToList();
        if (codes is not { Count: RecoveryCodeCount })
        {
            throw new InvalidOperationException("Generating recovery codes failed.");
        }

        trail.RecordBySelf(db, SecurityEvents.TwoFactorEnrolled, user);
        await CompleteSecondStepAsync(transaction, user, rememberDevice: false, new { method = "enrolment" });
        return (SignInOutcome.Done, codes);
    }

    private async Task<User?> PendingEnrolmentAsync()
    {
        var user = await signIn.GetTwoFactorAuthenticationUserAsync();
        return user is not null && IsPendingEnrolment(user) ? user : null;
    }

    private static bool IsPendingEnrolment(User user) => user is { Active: true, TwoFactorEnabled: false, PasswordHash: not null };

    /// <summary>Resets the counter, records the sign-in, commits, and only then issues the cookies.</summary>
    private async Task CompleteSecondStepAsync(IDbContextTransaction transaction, User user, bool rememberDevice, object data)
    {
        await users.ResetAccessFailedCountAsync(user).ThrowIfFailedAsync("Resetting failed attempts");
        await RecordSignInAsync(user, data);
        await transaction.CommitAsync(Persist);

        await signIn.SignOutPendingSecondStepAsync();
        if (rememberDevice)
        {
            await signIn.RememberTwoFactorClientAsync(user);
        }

        await signIn.SignInWithClaimsAsync(user, isPersistent: false, [MultiFactor]);
    }

    /// <summary>Records the sign-in with the new last-sign-in time and saves both.</summary>
    private async Task RecordSignInAsync(User user, object data)
    {
        user.LastSignInAt = time.GetUtcNow();
        trail.RecordBySelf(db, SecurityEvents.SignedIn, user, data);
        await users.UpdateAsync(user).ThrowIfFailedAsync("Recording the sign-in");
        await db.SaveChangesAsync(Persist);
    }

    private async Task<SignInOutcome> CountFailureAsync(IDbContextTransaction transaction, User user, string step)
    {
        await users.AccessFailedAsync(user).ThrowIfFailedAsync("Counting a failed code");
        var lockedOut = await users.IsLockedOutAsync(user);
        return await RejectCodeAsync(transaction, user, lockedOut ? SignInOutcome.LockedOut : SignInOutcome.InvalidCode, step, newlyLocked: lockedOut);
    }

    private async Task<SignInOutcome> RejectCodeAsync(IDbContextTransaction transaction, User user, SignInOutcome outcome, string step, bool newlyLocked)
    {
        trail.RecordAnonymous(db, newlyLocked ? SecurityEvents.LockedOut : SecurityEvents.SignInFailed, user, new { step });
        await CommitAsync(transaction);
        return outcome;
    }

    private async Task CommitAsync(IDbContextTransaction transaction)
    {
        await db.SaveChangesAsync(Persist);
        await transaction.CommitAsync(Persist);
    }

    private void VerifyDummyPassword(string password)
    {
        var dummy = new User { Name = "timing", Locale = "es-ES", Role = default, CreatedAt = default };
        users.PasswordHasher.VerifyHashedPassword(dummy, DummyHash.Get(users.PasswordHasher, dummy), password);
    }

    private static string Truncate(string? code)
    {
        var value = code ?? string.Empty;
        return value.Length > MaxCodeLength ? value[..MaxCodeLength] : value;
    }

    /// <summary>A hash of a random password with the configured hasher, computed once per process.</summary>
    private static class DummyHash
    {
        private static string? _value;

        public static string Get(IPasswordHasher<User> hasher, User user) =>
            _value ??= hasher.HashPassword(user, Guid.NewGuid().ToString());
    }
}
