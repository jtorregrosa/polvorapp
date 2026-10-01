using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PolvorApp.ArquebusierRegistry.Arquebusiers;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.SharedKernel.Http;
using PolvorApp.SharedKernel.Security;
using PolvorApp.SharedKernel.Time;
using PolvorApp.SharedKernel.Validation;

namespace PolvorApp.ArquebusierRegistry.Endpoints;

/// <summary>
/// Specs "Registering and editing arquebusiers", "Arquebusier visibility (BR-12)", "Transfer between
/// comparsas" and "Deleting an arquebusier" (design D6). Every operation is scoped to the caller's
/// comparsas on the server; an arquebusier outside the scope does not exist for the caller.
/// </summary>
internal static class ArquebusierEndpoints
{
    /// <summary>Largest request body the registry accepts.</summary>
    public const long MaxBodyBytes = 64 * 1024;

    public static IEndpointRouteBuilder MapArquebusierEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Registry bodies are a few hundred bytes; a larger one is refused before it is read (D11).
        var group = endpoints.MapGroup("/arquebusiers").WithTags("Arquebusiers")
            .WithMetadata(new RequestSizeLimitAttribute(MaxBodyBytes))
            .ProducesProblem(StatusCodes.Status401Unauthorized);
        group.MapGet("/", ListAsync).WithName("ListArquebusiers")
            .WithSummary("The arquebusiers of the caller's comparsas, sorted by last and first name.")
            .ProducesProblem(StatusCodes.Status400BadRequest);
        group.MapGet("/{id:guid}", GetAsync).WithName("GetArquebusier")
            .WithSummary("One arquebusier of the caller's comparsas, with license, course and owned weapons.")
            .ProducesProblem(StatusCodes.Status404NotFound);
        group.MapPost("/", RegisterAsync).WithName("RegisterArquebusier")
            .WithSummary("Registers an arquebusier in an active comparsa of the caller's scope.")
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .RequireRateLimiting(RateLimitPolicies.PersonalDataWrites).ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        group.MapPut("/{id:guid}", UpdateAsync).WithName("UpdateArquebusier")
            .WithSummary("Replaces the editable fields of an arquebusier, if the version is still current.")
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .RequireRateLimiting(RateLimitPolicies.PersonalDataWrites).ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        return endpoints;
    }

    private static async Task<Results<Ok<List<ArquebusierRowResponse>>, ProblemHttpResult>> ListAsync(
        Guid? comparsaId, string? status, ArquebusierQueries queries, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        var statusFilter = InputFields.OptionalCode<ArquebusierStatus>(status, "status", errors);
        return errors.Count > 0
            ? ProblemResults.Invalid(errors)
            : TypedResults.Ok(await queries.ListAsync(comparsaId, statusFilter, cancellationToken));
    }

    /// <summary>Out of scope and unknown look the same (BR-12): the arquebusier "does not exist" for the caller.</summary>
    private static async Task<Results<Ok<ArquebusierResponse>, ProblemHttpResult>> GetAsync(
        Guid id, ArquebusierQueries queries, ArquebusierViews views, CancellationToken cancellationToken) =>
        await queries.FindAsync(id, cancellationToken) is { } arquebusier
            ? TypedResults.Ok(await views.DetailAsync(arquebusier, cancellationToken))
            : RegistryProblems.From(RegistryOutcome.ArquebusierNotFound);

    private static async Task<Results<Created<ArquebusierResponse>, ProblemHttpResult>> RegisterAsync(
        RegisterArquebusierRequest request, ArquebusierAdministration administration, ArquebusierViews views, TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (!TryRead(request.Fields(), FederationCalendar.Today(time), statusRequired: false, out var input, out var errors))
        {
            if (request.ComparsaId is null)
            {
                errors["comparsaId"] = InputFields.Required;
            }

            return ProblemResults.Invalid(errors);
        }

        if (request.ComparsaId is not { } comparsaId)
        {
            return ProblemResults.Invalid(new Dictionary<string, string> { ["comparsaId"] = InputFields.Required });
        }

        return await administration.RegisterAsync(comparsaId, input, cancellationToken) switch
        {
            (RegistryOutcome.Done, { } arquebusier) =>
                TypedResults.Created($"/api/arquebusiers/{arquebusier.Id}", await views.DetailAsync(arquebusier, cancellationToken)),
            var (outcome, _) => RegistryProblems.From(outcome),
        };
    }

    private static async Task<Results<Ok<ArquebusierResponse>, ProblemHttpResult>> UpdateAsync(
        Guid id, UpdateArquebusierRequest request, ArquebusierAdministration administration, ArquebusierViews views, TimeProvider time,
        CancellationToken cancellationToken)
    {
        var valid = TryRead(request.Fields(), FederationCalendar.Today(time), statusRequired: true, out var input, out var errors);
        if (request.Version is null)
        {
            errors["version"] = InputFields.Required;
        }

        if (!valid || request.Version is not { } version)
        {
            return ProblemResults.Invalid(errors);
        }

        return await administration.UpdateAsync(id, input!, version, cancellationToken) switch
        {
            (RegistryOutcome.Done, { } arquebusier) => TypedResults.Ok(await views.DetailAsync(arquebusier, cancellationToken)),
            var (outcome, _) => RegistryProblems.From(outcome),
        };
    }

    private static bool TryRead(
        ArquebusierFields fields, DateOnly today, bool statusRequired,
        [NotNullWhen(true)] out ArquebusierInput? input, out Dictionary<string, string> errors)
    {
        var (parsed, found) = RegistryInput.Read(fields, today, statusRequired);
        (input, errors) = (parsed, new Dictionary<string, string>(found, StringComparer.Ordinal));
        return input is not null;
    }
}
