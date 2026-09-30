using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using PolvorApp.IdentityAccess.Persistence;
using PolvorApp.IdentityAccess.Security;
using PolvorApp.IdentityAccess.Users;
using PolvorApp.SharedKernel.Auditing;
using PolvorApp.SharedKernel.Security;

namespace PolvorApp.IdentityAccess.Endpoints;

/// <summary>Spec "Invitation-only accounts": the invitee sets a password and continues with enrolment.</summary>
internal static class InvitationEndpoints
{
    public static RouteGroupBuilder MapInvitationEndpoints(this RouteGroupBuilder auth)
    {
        auth.MapGet("/invitations/validate", ValidateAsync)
            .WithName("ValidateInvitation").WithSummary("The invitee's name and email for a valid link; 410 otherwise.")
            .AllowAnonymous().RequireRateLimiting(RateLimitPolicies.Auth)
            .ProducesProblem(StatusCodes.Status410Gone).ProducesProblem(StatusCodes.Status429TooManyRequests);
        auth.MapPost("/invitations/accept", AcceptAsync)
            .WithName("AcceptInvitation").WithSummary("Sets the invitee's password; the next step is enrolment.")
            .AllowAnonymous().RequireRateLimiting(RateLimitPolicies.Auth)
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status410Gone).ProducesProblem(StatusCodes.Status429TooManyRequests);
        return auth;
    }

    /// <summary>The id arrives as text: a link damaged by a mail client is just an invalid link (410).</summary>
    private static async Task<Results<Ok<InvitationResponse>, ProblemHttpResult>> ValidateAsync(
        string? user, string? token, UserManager<User> users) =>
        Guid.TryParse(user, out var userId) && await FindInviteeAsync(users, userId, token) is { } invitee
            ? TypedResults.Ok(new InvitationResponse(invitee.Name, invitee.Email ?? string.Empty))
            : Problems.Gone(Problems.InvalidLink);

    private static async Task<Results<Ok<LoginResponse>, ProblemHttpResult>> AcceptAsync(
        AcceptInvitationRequest request,
        UserManager<User> users,
        PolvorAppSignInManager signIn,
        IdentityAccessDbContext db,
        IAuditTrail trail,
        CancellationToken cancellationToken)
    {
        var invitee = await FindInviteeAsync(users, request.UserId, request.Token);
        if (invitee is null)
        {
            return Problems.Gone(Problems.InvalidLink);
        }

        await using var transaction = await db.LockAsync(invitee, CancellationToken.None);
        // Re-check under the lock: a parallel acceptance or a resend may have spent the link.
        if (invitee.Status != UserStatus.Invited
            || !await users.VerifyUserTokenAsync(invitee, InvitationTokenProvider.ProviderName, InvitationTokenProvider.Purpose, request.Token!))
        {
            return Problems.Gone(Problems.InvalidLink);
        }

        var result = await users.AddPasswordAsync(invitee, request.Password ?? string.Empty);
        if (!result.Succeeded)
        {
            return Problems.Password(result);
        }

        // Setting the password rotated the security stamp, so the link cannot be used again.
        trail.RecordBySelf(db, SecurityEvents.InvitationAccepted, invitee);
        await db.SaveChangesAsync(CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);
        await signIn.SignInPendingSecondStepAsync(invitee);
        return TypedResults.Ok(new LoginResponse(SignInStep.Enrol));
    }

    /// <summary>An active invited user whose link is valid (7 days, current stamp); null otherwise.</summary>
    private static async Task<User?> FindInviteeAsync(UserManager<User> users, Guid userId, string? token)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 2048)
        {
            return null;
        }

        var user = await users.FindByIdAsync(userId.ToString());
        return user is { Status: UserStatus.Invited }
            && await users.VerifyUserTokenAsync(user, InvitationTokenProvider.ProviderName, InvitationTokenProvider.Purpose, token)
                ? user
                : null;
    }
}
