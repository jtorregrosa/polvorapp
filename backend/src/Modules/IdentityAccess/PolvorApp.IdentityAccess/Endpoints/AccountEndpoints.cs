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

/// <summary>
/// Spec "Account self-service". Errors on these endpoints are 400, never 401: the UI treats a 401
/// as an expired session.
/// </summary>
internal static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var account = endpoints.MapGroup("/account").WithTags("Account").ProducesProblem(StatusCodes.Status401Unauthorized);
        account.MapGet("/", GetAccountAsync)
            .WithName("GetAccount").WithSummary("The signed-in user; 401 when there is no session.");
        account.MapPut("/locale", UpdateLocaleAsync)
            .WithName("UpdateLocale").WithSummary("Saves the preferred UI and email language.")
            .ProducesProblem(StatusCodes.Status400BadRequest);
        // Step-ups re-check a secret: rate limited and counted towards the lockout like sign-in.
        account.MapPost("/password", ChangePasswordAsync)
            .WithName("ChangePassword").WithSummary("Changes the password; other sessions and remembered devices end.")
            .RequireRateLimiting(RateLimitPolicies.Auth)
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status429TooManyRequests);
        account.MapPost("/recovery-codes", RegenerateRecoveryCodesAsync)
            .WithName("RegenerateRecoveryCodes").WithSummary("Replaces the recovery codes after a valid authenticator code.")
            .RequireRateLimiting(RateLimitPolicies.Auth)
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status429TooManyRequests);
        account.MapPost("/sign-out-everywhere", SignOutEverywhereAsync)
            .WithName("SignOutEverywhere").WithSummary("Ends every session and forgets every remembered device.");
        return endpoints;
    }

    /// <summary>The current user; also how the UI learns whether a session exists.</summary>
    private static async Task<Results<Ok<AccountResponse>, ProblemHttpResult>> GetAccountAsync(HttpContext context, UserManager<User> users)
    {
        var user = await users.GetUserAsync(context.User);
        return user is null
            ? Problems.Unauthorized(Problems.Unauthenticated)
            : TypedResults.Ok(new AccountResponse(user.Id, user.Name, user.Email ?? string.Empty, user.Role, user.Locale, await users.CountRecoveryCodesAsync(user)));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> UpdateLocaleAsync(
        UpdateLocaleRequest request, HttpContext context, UserManager<User> users, IdentityAccessDbContext db, IAuditTrail trail)
    {
        var errors = new Dictionary<string, string>();
        UserInput.Locale(request.Locale, errors);
        if (errors.Count > 0)
        {
            return Problems.Invalid(errors);
        }

        var user = await SignedInUserAsync(context, users);
        if (user.Locale != request.Locale)
        {
            trail.RecordBySelf(db, SecurityEvents.LocaleChanged, user, new { previous = user.Locale, current = request.Locale });
            user.Locale = request.Locale!;
            await users.UpdateAsync(user).ThrowIfFailedAsync("Saving the language");
        }

        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> ChangePasswordAsync(
        ChangePasswordRequest request,
        HttpContext context,
        UserManager<User> users,
        PolvorAppSignInManager signIn,
        IdentityAccessDbContext db,
        IAuditTrail trail,
        CancellationToken cancellationToken)
    {
        var user = await SignedInUserAsync(context, users);
        await using var transaction = await db.LockAsync(user, CancellationToken.None);
        if (await users.IsLockedOutAsync(user))
        {
            return Problems.Problem(StatusCodes.Status400BadRequest, Problems.LockedOut);
        }

        var result = await users.ChangePasswordAsync(user, request.CurrentPassword ?? string.Empty, request.NewPassword ?? string.Empty);
        if (!result.Succeeded)
        {
            if (!result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.PasswordMismatch)))
            {
                return Problems.Password(result);
            }

            return await RejectStepUpAsync(users, db, trail, transaction, user, "changePassword", Problems.WrongCurrentPassword);
        }

        await users.ResetAccessFailedCountAsync(user).ThrowIfFailedAsync("Resetting failed attempts");
        trail.RecordBySelf(db, SecurityEvents.PasswordChanged, user);
        await db.SaveChangesAsync(CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);

        // The new stamp ends the other sessions and every remembered device; this one continues.
        await signIn.RefreshSignInAsync(user);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<RecoveryCodesResponse>, ProblemHttpResult>> RegenerateRecoveryCodesAsync(
        RegenerateRecoveryCodesRequest request,
        HttpContext context,
        UserManager<User> users,
        IdentityAccessDbContext db,
        IAuditTrail trail,
        CancellationToken cancellationToken)
    {
        var user = await SignedInUserAsync(context, users);
        await using var transaction = await db.LockAsync(user, CancellationToken.None);
        if (await users.IsLockedOutAsync(user))
        {
            return Problems.Problem(StatusCodes.Status400BadRequest, Problems.LockedOut);
        }

        if (!await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, request.Code ?? string.Empty))
        {
            return await RejectStepUpAsync(users, db, trail, transaction, user, "recoveryCodes", Problems.InvalidCode);
        }

        var codes = (await users.GenerateNewTwoFactorRecoveryCodesAsync(user, SignInFlow.RecoveryCodeCount))?.ToList();
        if (codes is not { Count: SignInFlow.RecoveryCodeCount })
        {
            throw new InvalidOperationException("Generating recovery codes failed.");
        }

        await users.ResetAccessFailedCountAsync(user).ThrowIfFailedAsync("Resetting failed attempts");
        trail.RecordBySelf(db, SecurityEvents.RecoveryCodesRegenerated, user);
        await db.SaveChangesAsync(CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);
        return TypedResults.Ok(new RecoveryCodesResponse(codes));
    }

    private static async Task<NoContent> SignOutEverywhereAsync(
        HttpContext context, UserManager<User> users, PolvorAppSignInManager signIn, IdentityAccessDbContext db, IAuditTrail trail)
    {
        var user = await SignedInUserAsync(context, users);
        trail.RecordBySelf(db, SecurityEvents.SignedOutEverywhere, user);
        await users.UpdateSecurityStampAsync(user).ThrowIfFailedAsync("Ending every session");
        await signIn.ForgetTwoFactorClientAsync();
        await signIn.SignOutAsync();
        return TypedResults.NoContent();
    }

    /// <summary>
    /// A failed step-up counts towards the lockout and is audited, so a stolen session cannot guess
    /// the password or an authenticator code without limit.
    /// </summary>
    private static async Task<ProblemHttpResult> RejectStepUpAsync(
        UserManager<User> users, IdentityAccessDbContext db, IAuditTrail trail, Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        User user, string step, string code)
    {
        await users.AccessFailedAsync(user).ThrowIfFailedAsync("Counting a failed step-up");
        var lockedOut = await users.IsLockedOutAsync(user);
        trail.RecordBySelf(db, lockedOut ? SecurityEvents.LockedOut : SecurityEvents.SignInFailed, user, new { step });
        await db.SaveChangesAsync(CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);
        return Problems.Problem(StatusCodes.Status400BadRequest, lockedOut ? Problems.LockedOut : code);
    }

    /// <summary>The group requires a session, so the user exists; its absence is a server error.</summary>
    private static async Task<User> SignedInUserAsync(HttpContext context, UserManager<User> users) =>
        await users.GetUserAsync(context.User)
            ?? throw new InvalidOperationException("The signed-in user was not found.");
}
