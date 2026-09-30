using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using PolvorApp.FederationCatalog.Comparsas;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.FederationCatalog.Persistence;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.SharedKernel.Http;

namespace PolvorApp.FederationCatalog.Endpoints;

/// <summary>
/// Specs "Comparsas", "Comparsa management by Admins", "Comparsa visibility (BR-12)" and
/// "Deleting comparsas and weapon models". Reads are scoped to the caller's comparsas; every write
/// is Admin-only.
/// </summary>
internal static class ComparsaEndpoints
{
    public static IEndpointRouteBuilder MapComparsaEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/comparsas").WithTags("Comparsas").ProducesProblem(StatusCodes.Status401Unauthorized);
        group.MapGet("/", ListAsync).WithName("ListComparsas")
            .WithSummary("The comparsas the caller may see, sorted by name; inactive ones only with includeInactive.")
            .ProducesProblem(StatusCodes.Status400BadRequest);
        group.MapGet("/{id:guid}", GetAsync).WithName("GetComparsa").WithSummary("One comparsa within the caller's scope.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        var adminOnly = group.MapGroup(string.Empty).RequireAuthorization(AuthorizationPolicies.Admin).ProducesProblem(StatusCodes.Status403Forbidden);
        adminOnly.MapPost("/", CreateAsync).WithName("CreateComparsa").WithSummary("Creates a comparsa.")
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status409Conflict);
        adminOnly.MapPut("/{id:guid}", UpdateAsync).WithName("UpdateComparsa").WithSummary("Changes a comparsa's name and side.")
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
        adminOnly.MapPost("/{id:guid}/deactivate", (Guid id, ComparsaAdministration administration, CancellationToken ct) => SetActiveAsync(id, false, administration, ct))
            .WithName("DeactivateComparsa").WithSummary("Deactivates a comparsa; its assignments are kept.")
            .ProducesProblem(StatusCodes.Status404NotFound);
        adminOnly.MapPost("/{id:guid}/reactivate", (Guid id, ComparsaAdministration administration, CancellationToken ct) => SetActiveAsync(id, true, administration, ct))
            .WithName("ReactivateComparsa").WithSummary("Reactivates a deactivated comparsa.")
            .ProducesProblem(StatusCodes.Status404NotFound);
        adminOnly.MapDelete("/{id:guid}", DeleteAsync).WithName("DeleteComparsa")
            .WithSummary("Deletes a comparsa that no other record uses, with its FiringChief assignments.")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
        return endpoints;
    }

    private static async Task<Results<Ok<List<ComparsaResponse>>, ProblemHttpResult>> ListAsync(
        string? side, bool? includeInactive, FederationCatalogDbContext db, IComparsaScope scope, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string>();
        var sideFilter = CatalogInput.OptionalCode<Side>(side, "side", errors);
        if (errors.Count > 0)
        {
            return ProblemResults.Invalid(errors);
        }

        var access = await scope.GetAccessAsync(cancellationToken);
        var query = access.Filter(db.Comparsas.AsNoTracking(), c => c.Id);
        if (includeInactive != true)
        {
            query = query.Where(c => c.Active);
        }

        if (sideFilter is { } wanted)
        {
            query = query.Where(c => c.Side == wanted);
        }

        // About 20 rows: sorted here in Spanish order, whatever the database collation.
        var comparsas = await query.ToListAsync(cancellationToken);
        return TypedResults.Ok(comparsas.OrderBy(c => c.Name, CatalogOrder.Names).Select(ComparsaResponse.From).ToList());
    }

    /// <summary>Out of scope and unknown look the same (BR-12): the comparsa "does not exist" for the caller.</summary>
    private static async Task<Results<Ok<ComparsaResponse>, ProblemHttpResult>> GetAsync(
        Guid id, FederationCatalogDbContext db, IComparsaScope scope, CancellationToken cancellationToken)
    {
        var access = await scope.GetAccessAsync(cancellationToken);
        var comparsa = await access.Filter(db.Comparsas.AsNoTracking(), c => c.Id).SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        return comparsa is null ? CatalogProblems.From(CatalogOutcome.ComparsaNotFound) : TypedResults.Ok(ComparsaResponse.From(comparsa));
    }

    private static async Task<Results<Created<ComparsaResponse>, ProblemHttpResult>> CreateAsync(
        ComparsaRequest request, ComparsaAdministration administration, CancellationToken cancellationToken)
    {
        if (!TryRead(request, out var input, out var invalid))
        {
            return invalid;
        }

        return await administration.CreateAsync(input, cancellationToken) switch
        {
            (CatalogOutcome.Done, { } comparsa) => TypedResults.Created($"/api/comparsas/{comparsa.Id}", ComparsaResponse.From(comparsa)),
            var (outcome, _) => CatalogProblems.From(outcome),
        };
    }

    private static async Task<Results<Ok<ComparsaResponse>, ProblemHttpResult>> UpdateAsync(
        Guid id, ComparsaRequest request, ComparsaAdministration administration, CancellationToken cancellationToken)
    {
        if (!TryRead(request, out var input, out var invalid))
        {
            return invalid;
        }

        return Respond(await administration.UpdateAsync(id, input, cancellationToken));
    }

    private static async Task<Results<Ok<ComparsaResponse>, ProblemHttpResult>> SetActiveAsync(
        Guid id, bool active, ComparsaAdministration administration, CancellationToken cancellationToken) =>
        Respond(await administration.SetActiveAsync(id, active, cancellationToken));

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        Guid id, ComparsaAdministration administration, CancellationToken cancellationToken)
    {
        var outcome = await administration.DeleteAsync(id, cancellationToken);
        return outcome == CatalogOutcome.Done ? TypedResults.NoContent() : CatalogProblems.From(outcome);
    }

    private static Results<Ok<ComparsaResponse>, ProblemHttpResult> Respond((CatalogOutcome Outcome, Comparsa? Comparsa) result) => result switch
    {
        (CatalogOutcome.Done, { } comparsa) => TypedResults.Ok(ComparsaResponse.From(comparsa)),
        var (outcome, _) => CatalogProblems.From(outcome),
    };

    /// <summary>Reads a create or edit request, or names every invalid field.</summary>
    private static bool TryRead(
        ComparsaRequest request, [NotNullWhen(true)] out ComparsaInput? input, [NotNullWhen(false)] out ProblemHttpResult? invalid)
    {
        var errors = new Dictionary<string, string>();
        var name = CatalogInput.Text(request.Name, "name", Comparsa.NameMaxLength, errors);
        var side = CatalogInput.RequiredCode<Side>(request.Side, "side", errors);
        if (name is not null && side is { } validSide && errors.Count == 0)
        {
            (input, invalid) = (new ComparsaInput(name, validSide), null);
            return true;
        }

        (input, invalid) = (null, ProblemResults.Invalid(errors));
        return false;
    }
}
