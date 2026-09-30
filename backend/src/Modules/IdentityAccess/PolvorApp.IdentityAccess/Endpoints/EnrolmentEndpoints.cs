using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using PolvorApp.IdentityAccess.Security;
using PolvorApp.SharedKernel.Security;

namespace PolvorApp.IdentityAccess.Endpoints;

/// <summary>Spec "Mandatory two-factor enrolment".</summary>
internal static class EnrolmentEndpoints
{
    private const string Issuer = "PolvorApp";

    public static RouteGroupBuilder MapEnrolmentEndpoints(this RouteGroupBuilder auth)
    {
        auth.MapGet("/enrolment", GetEnrolmentAsync)
            .WithName("GetEnrolment").WithSummary("Authenticator key and otpauth URI for the pending enrolment.")
            .AllowAnonymous().RequireRateLimiting(RateLimitPolicies.Auth)
            .ProducesProblem(StatusCodes.Status401Unauthorized);
        auth.MapPost("/enrolment", ConfirmEnrolmentAsync)
            .WithName("ConfirmEnrolment").WithSummary("Confirms the authenticator, returns the recovery codes once and signs in.")
            .AllowAnonymous().RequireRateLimiting(RateLimitPolicies.Auth)
            .ProducesProblem(StatusCodes.Status401Unauthorized).ProducesProblem(StatusCodes.Status429TooManyRequests);
        return auth;
    }

    private static async Task<Results<Ok<EnrolmentResponse>, ProblemHttpResult>> GetEnrolmentAsync(
        SignInFlow flow, CancellationToken cancellationToken)
    {
        if (await flow.EnrolmentKeyAsync(cancellationToken) is not var (key, email))
        {
            return Problems.Unauthorized(Problems.StepExpired);
        }

        var label = Uri.EscapeDataString($"{Issuer}:{email}");
        var uri = $"otpauth://totp/{label}?secret={key}&issuer={Issuer}&digits={Totp.Digits}&period={(int)Totp.Step.TotalSeconds}";
        return TypedResults.Ok(new EnrolmentResponse(Group(key), uri));
    }

    private static async Task<Results<Ok<RecoveryCodesResponse>, ProblemHttpResult>> ConfirmEnrolmentAsync(
        EnrolmentConfirmRequest request, SignInFlow flow, CancellationToken cancellationToken)
    {
        var (outcome, codes) = await flow.ConfirmEnrolmentAsync(request.Code, cancellationToken);
        return outcome == SignInOutcome.Done
            ? TypedResults.Ok(new RecoveryCodesResponse(codes))
            : SignInEndpoints.CodeProblem(outcome);
    }

    private static string Group(string key)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < key.Length; i += 4)
        {
            builder.Append(key.AsSpan(i, Math.Min(4, key.Length - i))).Append(' ');
        }

        return builder.ToString().TrimEnd().ToLowerInvariant();
    }
}
