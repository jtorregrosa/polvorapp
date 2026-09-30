using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using PolvorApp.IdentityAccess.Security;
using PolvorApp.SharedKernel.Security;

namespace PolvorApp.IdentityAccess.Endpoints;

/// <summary>Spec "Sign-in with two-factor authentication" and "Remembered devices" (design D5).</summary>
internal static class SignInEndpoints
{
    public static RouteGroupBuilder MapSignInEndpoints(this RouteGroupBuilder auth)
    {
        auth.MapPost("/login", LoginAsync)
            .WithName("Login").WithSummary("Checks email and password and tells the UI the next step.")
            .AllowAnonymous().RequireRateLimiting(RateLimitPolicies.Auth)
            .ProducesProblem(StatusCodes.Status401Unauthorized).ProducesProblem(StatusCodes.Status429TooManyRequests);
        auth.MapPost("/login/second-factor", SecondFactorAsync)
            .WithName("LoginSecondFactor").WithSummary("Completes sign-in with an authenticator code.")
            .AllowAnonymous().RequireRateLimiting(RateLimitPolicies.Auth)
            .ProducesProblem(StatusCodes.Status401Unauthorized).ProducesProblem(StatusCodes.Status429TooManyRequests);
        auth.MapPost("/login/recovery-code", RecoveryCodeAsync)
            .WithName("LoginRecoveryCode").WithSummary("Completes sign-in with a single-use recovery code.")
            .AllowAnonymous().RequireRateLimiting(RateLimitPolicies.Auth)
            .ProducesProblem(StatusCodes.Status401Unauthorized).ProducesProblem(StatusCodes.Status429TooManyRequests);
        auth.MapPost("/logout", LogoutAsync)
            .WithName("Logout").WithSummary("Ends the session on this browser (the remembered device is kept).")
            .AllowAnonymous();
        return auth;
    }

    private static async Task<Results<Ok<LoginResponse>, ProblemHttpResult>> LoginAsync(
        LoginRequest request, SignInFlow flow, CancellationToken cancellationToken) =>
        await flow.PasswordAsync(request.Email, request.Password, cancellationToken) switch
        {
            SignInOutcome.Done => TypedResults.Ok(new LoginResponse(SignInStep.Done)),
            SignInOutcome.SecondFactor => TypedResults.Ok(new LoginResponse(SignInStep.SecondFactor)),
            SignInOutcome.Enrol => TypedResults.Ok(new LoginResponse(SignInStep.Enrol)),
            SignInOutcome.InvalidCredentials => Problems.Unauthorized(Problems.InvalidCredentials),
            var other => throw new System.Diagnostics.UnreachableException($"The password step does not end in {other}."),
        };

    private static async Task<Results<Ok<LoginResponse>, ProblemHttpResult>> SecondFactorAsync(
        SecondFactorRequest request, SignInFlow flow, CancellationToken cancellationToken)
    {
        var outcome = await flow.AuthenticatorCodeAsync(request.Code, request.RememberDevice, cancellationToken);
        return outcome == SignInOutcome.Done ? TypedResults.Ok(new LoginResponse(SignInStep.Done)) : CodeProblem(outcome);
    }

    private static async Task<Results<Ok<RecoveryCodeResponse>, ProblemHttpResult>> RecoveryCodeAsync(
        RecoveryCodeRequest request, SignInFlow flow, CancellationToken cancellationToken)
    {
        var (outcome, left) = await flow.RecoveryCodeAsync(request.Code, cancellationToken);
        return outcome == SignInOutcome.Done ? TypedResults.Ok(new RecoveryCodeResponse(SignInStep.Done, left)) : CodeProblem(outcome);
    }

    /// <summary>Also clears a pending second step, so it needs no session.</summary>
    private static async Task<NoContent> LogoutAsync(PolvorAppSignInManager signIn)
    {
        await signIn.SignOutAsync();
        return TypedResults.NoContent();
    }

    internal static ProblemHttpResult CodeProblem(SignInOutcome outcome) => outcome switch
    {
        SignInOutcome.LockedOut => Problems.Unauthorized(Problems.LockedOut),
        SignInOutcome.StepExpired => Problems.Unauthorized(Problems.StepExpired),
        _ => Problems.Unauthorized(Problems.InvalidCode),
    };
}
