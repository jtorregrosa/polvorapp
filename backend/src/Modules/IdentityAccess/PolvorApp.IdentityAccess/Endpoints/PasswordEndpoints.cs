using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PolvorApp.IdentityAccess.Emails;
using PolvorApp.IdentityAccess.Persistence;
using PolvorApp.IdentityAccess.Security;
using PolvorApp.IdentityAccess.Users;
using PolvorApp.SharedKernel.Auditing;
using PolvorApp.SharedKernel.Security;

namespace PolvorApp.IdentityAccess.Endpoints;

/// <summary>
/// Spec "Password reset by email". Only users who have enrolled two-factor authentication can
/// reset a password: for anyone else a reset would let whoever controls the mailbox enrol their
/// own authenticator (group 5 security review). The email is queued, so the answer takes the same
/// time whether or not the account exists.
/// </summary>
internal static partial class PasswordEndpoints
{
    /// <summary>Every request takes at least this long, so timing does not reveal whether the email exists.</summary>
    private static readonly TimeSpan MinimumDuration = TimeSpan.FromMilliseconds(400);

    /// <summary>At most one reset email per account in this period, whatever the client address.</summary>
    private static readonly TimeSpan PerAccountInterval = TimeSpan.FromMinutes(5);

    public static RouteGroupBuilder MapPasswordEndpoints(this RouteGroupBuilder auth)
    {
        auth.MapPost("/password/forgot", ForgotAsync)
            .WithName("ForgotPassword").WithSummary("Emails a single-use reset link; the answer is the same for every email.")
            .AllowAnonymous().RequireRateLimiting(RateLimitPolicies.AuthEmail)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
        auth.MapPost("/password/reset", ResetAsync)
            .WithName("ResetPassword").WithSummary("Sets a new password with an emailed link; the second factor is still required.")
            .AllowAnonymous().RequireRateLimiting(RateLimitPolicies.Auth)
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status410Gone);
        return auth;
    }

    private static async Task<Accepted> ForgotAsync(
        ForgotPasswordRequest request,
        UserManager<User> users,
        IdentityAccessDbContext db,
        IAuditTrail trail,
        IdentityEmails emails,
        TimeProvider time,
        ILogger<IdentityEmails> logger,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            await RequestResetAsync(request.Email, users, db, trail, emails, time);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Any failure must look like success: a different answer would reveal the account.
            LogResetRequestFailed(logger, exception.GetType().Name);
        }

        var remaining = MinimumDuration - Stopwatch.GetElapsedTime(started);
        if (remaining > TimeSpan.Zero)
        {
            await Task.Delay(remaining, cancellationToken);
        }

        return TypedResults.Accepted((string?)null);
    }

    private static async Task RequestResetAsync(
        string? email, UserManager<User> users, IdentityAccessDbContext db, IAuditTrail trail, IdentityEmails emails, TimeProvider time)
    {
        email = (email ?? string.Empty).Trim();
        if (email.Length is 0 or > SignInFlow.MaxEmailLength
            || await users.FindByEmailAsync(email) is not { } user
            || !CanReset(user))
        {
            return;
        }

        var since = time.GetUtcNow() - PerAccountInterval;
        var entityId = user.Id.ToString();
        if (await db.Set<AuditEntry>().AnyAsync(
            e => e.Action == SecurityEvents.PasswordResetRequested && e.EntityId == entityId && e.OccurredAt > since))
        {
            return;
        }

        var token = await users.GeneratePasswordResetTokenAsync(user);
        trail.RecordAnonymous(db, SecurityEvents.PasswordResetRequested, user);
        await db.SaveChangesAsync(CancellationToken.None);
        emails.QueuePasswordReset(user, token);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> ResetAsync(
        ResetPasswordRequest request,
        UserManager<User> users,
        IdentityAccessDbContext db,
        IAuditTrail trail)
    {
        var user = await users.FindByIdAsync(request.UserId.ToString());
        if (user is null || !CanReset(user))
        {
            return Problems.Gone(Problems.InvalidLink);
        }

        await using var transaction = await db.LockAsync(user, CancellationToken.None);
        if (!CanReset(user))
        {
            return Problems.Gone(Problems.InvalidLink);
        }

        var result = await users.ResetPasswordAsync(user, request.Token ?? string.Empty, request.Password ?? string.Empty);
        if (!result.Succeeded)
        {
            return result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.InvalidToken))
                ? Problems.Gone(Problems.InvalidLink)
                : Problems.Password(result);
        }

        // The new security stamp ends every session, remembered device and pending step; the next
        // sign-in still needs the second factor.
        trail.RecordAnonymous(db, SecurityEvents.PasswordReset, user);
        await db.SaveChangesAsync(CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);
        return TypedResults.NoContent();
    }

    private static bool CanReset(User user) => user is { Active: true, PasswordHash: not null, TwoFactorEnabled: true };

    [LoggerMessage(Level = LogLevel.Error, Message = "A password-reset request failed ({ErrorType}); it was answered as usual")]
    private static partial void LogResetRequestFailed(ILogger logger, string errorType);
}
