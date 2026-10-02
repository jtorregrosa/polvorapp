using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PolvorApp.ArquebusierRegistry.Lock;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.SharedKernel.Http;
using PolvorApp.SharedKernel.Validation;

namespace PolvorApp.ArquebusierRegistry.Endpoints;

/// <summary>Whether the registry is locked for FiringChiefs, and since when.</summary>
/// <param name="Locked">True while FiringChiefs cannot change the registry.</param>
/// <param name="ChangedAt">When the lock was last turned on or off; null until then.</param>
internal sealed record RegistryLockResponse(bool Locked, DateTimeOffset? ChangedAt)
{
    public static RegistryLockResponse From(RegistrySettings settings) => new(settings.Locked, settings.LockedChangedAt);
}

/// <summary>Lock or unlock the registry.</summary>
/// <param name="Locked">True locks it for FiringChiefs; false unlocks it.</param>
internal sealed record SetRegistryLockRequest(bool? Locked);

/// <summary>
/// Spec "Registry lock (BR-10, UC-11)" (add-festival-editions, design D8): every signed-in user reads
/// the lock; only Admins change it. A group of its own, outside <c>/arquebusiers</c>.
/// </summary>
internal static class RegistryLockEndpoints
{
    public static IEndpointRouteBuilder MapRegistryLockEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/registry/lock").WithTags("Registry")
            .WithMetadata(new RequestSizeLimitAttribute(ArquebusierEndpoints.MaxBodyBytes))
            .ProducesProblem(StatusCodes.Status401Unauthorized);
        group.MapGet("/", GetAsync).WithName("GetRegistryLock")
            .WithSummary("Whether the registry is locked for FiringChiefs.");
        group.MapPut("/", SetAsync).WithName("SetRegistryLock")
            .WithSummary("Locks or unlocks the registry for FiringChiefs.")
            .RequireAuthorization(AuthorizationPolicies.Admin)
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        return endpoints;
    }

    private static async Task<Ok<RegistryLockResponse>> GetAsync(RegistryLockAdministration administration, CancellationToken cancellationToken) =>
        TypedResults.Ok(RegistryLockResponse.From(await administration.ReadAsync(cancellationToken)));

    private static async Task<Results<Ok<RegistryLockResponse>, ProblemHttpResult>> SetAsync(
        SetRegistryLockRequest request, RegistryLockAdministration administration, CancellationToken cancellationToken)
    {
        if (request.Locked is not { } locked)
        {
            return ProblemResults.Invalid(new Dictionary<string, string> { ["locked"] = InputFields.Required });
        }

        return await administration.SetAsync(locked, cancellationToken) switch
        {
            (RegistryOutcome.Done, { } settings) => TypedResults.Ok(RegistryLockResponse.From(settings)),
            var (outcome, _) => RegistryProblems.From(outcome),
        };
    }
}
