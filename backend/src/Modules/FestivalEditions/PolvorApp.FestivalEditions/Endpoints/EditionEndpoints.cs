using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PolvorApp.FestivalEditions.Contracts;
using PolvorApp.FestivalEditions.Editions;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.SharedKernel.Http;
using PolvorApp.SharedKernel.Security;
using PolvorApp.SharedKernel.Validation;

namespace PolvorApp.FestivalEditions.Endpoints;

/// <summary>
/// Specs "Festival editions (UC-10)", "Edition management by Admins", "Edition visibility (BR-12)"
/// and "Current edition" (design D4). Every signed-in user reads; every write is Admin-only.
/// </summary>
internal static class EditionEndpoints
{
    /// <summary>Validation reason for a list longer than its limit.</summary>
    public const string TooMany = "tooMany";

    /// <summary>Largest write body: a set of 100 model ids fits with room to spare.</summary>
    public const long MaxRequestBytes = 16 * 1024;

    public static IEndpointRouteBuilder MapEditionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/editions").WithTags("Editions").ProducesProblem(StatusCodes.Status401Unauthorized);
        group.MapGet("/", ListAsync).WithName("ListEditions")
            .WithSummary("The editions the caller can see, newest first; FiringChiefs never see drafts.");
        group.MapGet("/current", CurrentAsync).WithName("GetCurrentEdition")
            .WithSummary("The edition in progress, or null when there is none.");
        group.MapGet("/{id:guid}", GetAsync).WithName("GetEdition").WithSummary("One edition.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        var adminOnly = group.MapGroup(string.Empty)
            .RequireAuthorization(AuthorizationPolicies.Admin)
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBytes))
            .ProducesProblem(StatusCodes.Status403Forbidden);
        adminOnly.MapPost("/", CreateAsync).WithName("CreateEdition")
            .WithSummary("Creates a draft edition with the prices and rental models of the previous one.")
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        adminOnly.MapPut("/{id:guid}", UpdateAsync).WithName("UpdateEdition")
            .WithSummary("Changes an edition's festival dates, order window and prices.")
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        adminOnly.MapPost("/{id:guid}/status", ChangeStatusAsync).WithName("ChangeEditionStatus")
            .WithSummary("Moves an edition one step: DRAFT, IN_PROGRESS, CLOSED.")
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        // Each change emails every FiringChief (add-notifications): rate limited like the order moves.
        adminOnly.MapPost("/{id:guid}/orders", SetOrdersAsync).WithName("SetEditionOrders")
            .RequireRateLimiting(RateLimitPolicies.OrderWrites).ProducesProblem(StatusCodes.Status429TooManyRequests)
            .WithSummary("Opens or closes the orders of the edition in progress.")
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        adminOnly.MapPut("/{id:guid}/weapon-models", SetWeaponModelsAsync).WithName("SetEditionWeaponModels")
            .WithSummary("Replaces the set of rental models offered in an edition.")
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        adminOnly.MapDelete("/{id:guid}", DeleteAsync).WithName("DeleteEdition")
            .WithSummary("Deletes a draft edition with its rental models and milestones.")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        return endpoints;
    }

    private static async Task<Ok<List<EditionRowResponse>>> ListAsync(EditionViews views, CancellationToken cancellationToken) =>
        TypedResults.Ok(await views.ListAsync(cancellationToken));

    private static async Task<Ok<CurrentEditionResponse>> CurrentAsync(EditionViews views, CancellationToken cancellationToken) =>
        TypedResults.Ok(new CurrentEditionResponse(await views.CurrentAsync(cancellationToken)));

    private static async Task<Results<Ok<EditionResponse>, ProblemHttpResult>> GetAsync(Guid id, EditionViews views, CancellationToken cancellationToken) =>
        await views.FindAsync(id, cancellationToken) is { } edition
            ? TypedResults.Ok(edition)
            : EditionProblems.From(EditionOutcome.NotFound);

    private static async Task<Results<Created<EditionResponse>, ProblemHttpResult>> CreateAsync(
        CreateEditionRequest request, EditionAdministration administration, EditionViews views, CancellationToken cancellationToken)
    {
        var (year, yearErrors) = EditionInput.ReadYear(request.Year);
        var errors = new Dictionary<string, string>(yearErrors, StringComparer.Ordinal);
        EditionInput? input = null;
        if (year is { } validYear)
        {
            (input, var dateErrors) = EditionInput.Read(validYear, new EditionFields(request.FestivalStartsOn, request.FestivalEndsOn, null, null, null));
            foreach (var (field, reason) in dateErrors)
            {
                errors[field] = reason;
            }
        }
        else
        {
            // Without a valid year the dates can only be checked for presence and format.
            InputFields.RequiredDate(request.FestivalStartsOn, "festivalStartsOn", errors);
            InputFields.RequiredDate(request.FestivalEndsOn, "festivalEndsOn", errors);
        }

        if (errors.Count > 0 || input is null || year is null)
        {
            return ProblemResults.Invalid(errors);
        }

        var result = await administration.CreateAsync(year.Value, input.FestivalStartsOn, input.FestivalEndsOn, cancellationToken);
        return result is { Outcome: EditionOutcome.Done, Edition: { } edition }
            ? TypedResults.Created($"/api/editions/{edition.Id}", await views.ToResponseAsync(edition, cancellationToken))
            : EditionProblems.From(result);
    }

    private static async Task<Results<Ok<EditionResponse>, ProblemHttpResult>> UpdateAsync(
        Guid id, UpdateEditionRequest request, EditionAdministration administration, EditionViews views, CancellationToken cancellationToken)
    {
        if (request.Version is not { } version)
        {
            return ProblemResults.Invalid(new Dictionary<string, string> { ["version"] = InputFields.Required });
        }

        var prices = request.Prices is { } p ? new PriceFields(p.PowderPerKg, p.CapsBox, p.WeaponRental, p.FlaskRental) : null;
        var fields = new EditionFields(request.FestivalStartsOn, request.FestivalEndsOn, request.OrdersOpenOn, request.OrdersCloseOn, prices);
        return await RespondAsync(await administration.UpdateAsync(id, fields, version, cancellationToken), views, cancellationToken);
    }

    private static async Task<Results<Ok<EditionResponse>, ProblemHttpResult>> ChangeStatusAsync(
        Guid id, ChangeEditionStatusRequest request, EditionLifecycle lifecycle, EditionViews views, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        var status = InputFields.RequiredCode<EditionStatus>(request.Status, "status", errors);
        if (request.Version is null)
        {
            errors["version"] = InputFields.Required;
        }

        if (status is not { } to || request.Version is not { } version || errors.Count > 0)
        {
            return ProblemResults.Invalid(errors);
        }

        return await RespondAsync(await lifecycle.ChangeStatusAsync(id, to, version, cancellationToken), views, cancellationToken);
    }

    private static async Task<Results<Ok<EditionResponse>, ProblemHttpResult>> SetOrdersAsync(
        Guid id, SetEditionOrdersRequest request, EditionLifecycle lifecycle, EditionViews views, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        if (request.Open is null)
        {
            errors["open"] = InputFields.Required;
        }

        if (request.Version is null)
        {
            errors["version"] = InputFields.Required;
        }

        if (request.Open is not { } open || request.Version is not { } version)
        {
            return ProblemResults.Invalid(errors);
        }

        return await RespondAsync(await lifecycle.SetOrdersAsync(id, open, version, cancellationToken), views, cancellationToken);
    }

    private static async Task<Results<Ok<EditionResponse>, ProblemHttpResult>> SetWeaponModelsAsync(
        Guid id, EditionWeaponModelsRequest request, EditionWeaponModelAdministration administration, EditionViews views, CancellationToken cancellationToken)
    {
        if (request.WeaponModelIds is not { } ids)
        {
            return ProblemResults.Invalid(new Dictionary<string, string> { ["weaponModelIds"] = InputFields.Required });
        }

        if (ids.Count > EditionWeaponModelAdministration.MaxModels)
        {
            return ProblemResults.Invalid(new Dictionary<string, string> { ["weaponModelIds"] = TooMany });
        }

        return await RespondAsync(await administration.SetAsync(id, [.. ids], cancellationToken), views, cancellationToken);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        Guid id, EditionAdministration administration, CancellationToken cancellationToken)
    {
        var result = await administration.DeleteAsync(id, cancellationToken);
        return result.Outcome == EditionOutcome.Done ? TypedResults.NoContent() : EditionProblems.From(result);
    }

    /// <summary>The saved edition, or the problem of a failed write.</summary>
    internal static async Task<Results<Ok<EditionResponse>, ProblemHttpResult>> RespondAsync(
        EditionWrite result, EditionViews views, CancellationToken cancellationToken) =>
        result is { Outcome: EditionOutcome.Done, Edition: { } edition }
            ? TypedResults.Ok(await views.ToResponseAsync(edition, cancellationToken))
            : EditionProblems.From(result);
}
