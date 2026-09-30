using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using PolvorApp.FederationCatalog.Assignments;
using PolvorApp.FederationCatalog.Persistence;
using PolvorApp.IdentityAccess.Contracts;

namespace PolvorApp.FederationCatalog.Endpoints;

/// <summary>
/// Specs "FiringChief assignments" and "Managing assignments from the comparsa and from the user".
/// Admin-only, including a FiringChief's own comparsa. Both UI directions share these writes.
/// </summary>
internal static class AssignmentEndpoints
{
    public static IEndpointRouteBuilder MapAssignmentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var assignments = endpoints.MapGroup("/comparsas/{id:guid}/firing-chiefs").WithTags("FiringChiefAssignments")
            .RequireAuthorization(AuthorizationPolicies.Admin)
            .ProducesProblem(StatusCodes.Status401Unauthorized).ProducesProblem(StatusCodes.Status403Forbidden);

        assignments.MapGet("/", ListFiringChiefsAsync).WithName("ListFiringChiefs")
            .WithSummary("The comparsa's FiringChiefs with name, email and status, sorted by name.")
            .ProducesProblem(StatusCodes.Status404NotFound);
        assignments.MapPut("/{userId:guid}", AssignAsync).WithName("AssignFiringChief")
            .WithSummary("Assigns a FiringChief to the comparsa; repeating it changes nothing.")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
        assignments.MapDelete("/{userId:guid}", UnassignAsync).WithName("UnassignFiringChief")
            .WithSummary("Removes a FiringChief from the comparsa; removing a missing assignment changes nothing.")
            .ProducesProblem(StatusCodes.Status404NotFound);
        endpoints.MapGet("/firing-chiefs/{userId:guid}/comparsas", ListComparsasOfAsync).WithTags("FiringChiefAssignments")
            .RequireAuthorization(AuthorizationPolicies.Admin).WithName("ListFiringChiefComparsas")
            .WithSummary("The comparsas a user is assigned to, active or not, sorted by name.")
            .ProducesProblem(StatusCodes.Status401Unauthorized).ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
        return endpoints;
    }

    /// <summary>Users unknown to the directory are skipped: users are never deleted, so this is defensive only.</summary>
    private static async Task<Results<Ok<List<FiringChiefResponse>>, ProblemHttpResult>> ListFiringChiefsAsync(
        Guid id, FederationCatalogDbContext db, IUserDirectory users, CancellationToken cancellationToken)
    {
        if (!await db.Comparsas.AnyAsync(c => c.Id == id, cancellationToken))
        {
            return CatalogProblems.From(CatalogOutcome.ComparsaNotFound);
        }

        var userIds = await db.Assignments.AsNoTracking().Where(a => a.ComparsaId == id).Select(a => a.UserId).ToListAsync(cancellationToken);
        var chiefs = await users.FindManyAsync(userIds, cancellationToken);
        return TypedResults.Ok(chiefs
            .OrderBy(u => u.Name, CatalogOrder.Names)
            .Select(u => new FiringChiefResponse(u.Id, u.Name, u.Email, u.Status))
            .ToList());
    }

    private static async Task<Results<Ok<List<ComparsaResponse>>, ProblemHttpResult>> ListComparsasOfAsync(
        Guid userId, FederationCatalogDbContext db, IUserDirectory users, CancellationToken cancellationToken)
    {
        if (await users.FindAsync(userId, cancellationToken) is null)
        {
            return CatalogProblems.From(CatalogOutcome.UserNotFound);
        }

        var comparsas = await db.Comparsas.AsNoTracking()
            .Where(c => db.Assignments.Any(a => a.ComparsaId == c.Id && a.UserId == userId))
            .ToListAsync(cancellationToken);
        return TypedResults.Ok(comparsas.OrderBy(c => c.Name, CatalogOrder.Names).Select(ComparsaResponse.From).ToList());
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> AssignAsync(
        Guid id, Guid userId, AssignmentAdministration administration, CancellationToken cancellationToken) =>
        Respond(await administration.AssignAsync(id, userId, cancellationToken));

    private static async Task<Results<NoContent, ProblemHttpResult>> UnassignAsync(
        Guid id, Guid userId, AssignmentAdministration administration, CancellationToken cancellationToken) =>
        Respond(await administration.UnassignAsync(id, userId, cancellationToken));

    private static Results<NoContent, ProblemHttpResult> Respond(CatalogOutcome outcome) =>
        outcome == CatalogOutcome.Done ? TypedResults.NoContent() : CatalogProblems.From(outcome);
}
