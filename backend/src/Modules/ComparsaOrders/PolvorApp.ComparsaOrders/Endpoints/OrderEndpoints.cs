using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PolvorApp.ComparsaOrders.Entries;
using PolvorApp.ComparsaOrders.Loans;
using PolvorApp.ComparsaOrders.Orders;
using PolvorApp.ComparsaOrders.Totals;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.SharedKernel.Http;
using PolvorApp.SharedKernel.Security;
using PolvorApp.SharedKernel.Validation;

namespace PolvorApp.ComparsaOrders.Endpoints;

/// <summary>
/// Specs "Preparing an order (UC-12)", "Order visibility (BR-12)" and the order writes (design D6).
/// Routes sit under <c>/comparsa-orders</c>, apart from <c>/editions/{id}/orders</c>, which opens and
/// closes the orders. Every signed-in user may call them; scope, role and BR-10 are checked inside.
/// </summary>
internal static class OrderEndpoints
{
    /// <summary>Largest write body: an entry with an external lender fits with room to spare.</summary>
    public const long MaxRequestBytes = 16 * 1024;

    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/comparsa-orders").WithTags("ComparsaOrders")
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBytes))
            .ProducesProblem(StatusCodes.Status401Unauthorized);
        group.MapPost("/", PrepareAsync).WithName("PrepareComparsaOrder")
            .WithSummary("Prepares the order of a comparsa for an edition, with one pre-filled entry per arquebusier.")
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .RequireRateLimiting(RateLimitPolicies.OrderWrites).ProducesProblem(StatusCodes.Status429TooManyRequests);
        group.MapPost("/{id:guid}/submit", SubmitAsync).WithName("SubmitComparsaOrder")
            .WithSummary("Submits a draft or returned order: a FiringChief with the attestation while the orders are open, an Admin on the comparsa's behalf.")
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .RequireRateLimiting(RateLimitPolicies.OrderWrites).ProducesProblem(StatusCodes.Status429TooManyRequests);
        var review = group.MapGroup(string.Empty)
            .RequireAuthorization(AuthorizationPolicies.Admin)
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .RequireRateLimiting(RateLimitPolicies.OrderWrites).ProducesProblem(StatusCodes.Status429TooManyRequests);
        review.MapPost("/{id:guid}/validate", ValidateAsync).WithName("ValidateComparsaOrder")
            .WithSummary("Validates a submitted order, or closes one the comparsa never submitted (Admins).");
        review.MapPost("/{id:guid}/return", ReturnAsync).WithName("ReturnComparsaOrder")
            .WithSummary("Returns a submitted or validated order with a reason (Admins).");
        group.MapPost("/lender-lookup", LookUpLenderAsync).WithName("LookUpLender")
            .WithSummary("Finds a registered lender by an exact DNI/NIE, with their weapons (no guides); audited.")
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .RequireRateLimiting(RateLimitPolicies.PersonalDataWrites).ProducesProblem(StatusCodes.Status429TooManyRequests);
        group.MapGet("/overview", OverviewAsync).WithName("GetComparsaOrdersOverview")
            .WithSummary("The orders of an edition (the current one by default): each comparsa's status and totals; for Admins also the counts and edition totals.")
            .ProducesProblem(StatusCodes.Status404NotFound);
        group.MapGet("/{id:guid}", GetAsync).WithName("GetComparsaOrder")
            .WithSummary("One order of the caller's comparsas, with its entries and the arquebusiers not in it.")
            .ProducesProblem(StatusCodes.Status404NotFound);
        group.MapPost("/{id:guid}/entries", AddEntryAsync).WithName("AddEditionEntry")
            .WithSummary("Adds a pre-filled entry for an arquebusier of the comparsa who has none in the edition; returns the order.")
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .RequireRateLimiting(RateLimitPolicies.OrderWrites).ProducesProblem(StatusCodes.Status429TooManyRequests);
        group.MapPut("/{id:guid}/entries/{entryId:guid}", UpdateEntryAsync).WithName("UpdateEditionEntry")
            .WithSummary("Replaces the values and the loan of an entry, if its version is still current; returns the order.")
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .RequireRateLimiting(RateLimitPolicies.OrderWrites).ProducesProblem(StatusCodes.Status429TooManyRequests);
        return endpoints;
    }

    private static async Task<Results<Created<OrderResponse>, ProblemHttpResult>> PrepareAsync(
        PrepareOrderRequest request, OrderAdministration administration, OrderViews views, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        if (request.EditionId is null)
        {
            errors["editionId"] = InputFields.Required;
        }

        if (request.ComparsaId is null)
        {
            errors["comparsaId"] = InputFields.Required;
        }

        if (errors.Count > 0)
        {
            return ProblemResults.Invalid(errors);
        }

        var result = await administration.PrepareAsync(request.EditionId!.Value, request.ComparsaId!.Value, cancellationToken);
        if (result is not { Outcome: OrderOutcome.Done, Value: { } order })
        {
            return result.Problem();
        }

        var response = await views.FindAsync(order.Id, cancellationToken)
            ?? throw new InvalidOperationException($"Order {order.Id} was committed but cannot be read back.");
        return TypedResults.Created($"/api/comparsa-orders/{order.Id}", response);
    }

    private static async Task<Results<Ok<OrderResponse>, ProblemHttpResult>> AddEntryAsync(
        Guid id, AddEntryRequest request, EntryAdministration administration, OrderViews views, CancellationToken cancellationToken)
    {
        if (request.ArquebusierId is not { } arquebusierId)
        {
            return ProblemResults.Invalid(new Dictionary<string, string> { ["arquebusierId"] = InputFields.Required });
        }

        return await RespondAsync(await administration.AddAsync(id, arquebusierId, cancellationToken), views, cancellationToken);
    }

    private static async Task<Results<Ok<OrderResponse>, ProblemHttpResult>> UpdateEntryAsync(
        Guid id,
        Guid entryId,
        EditEntryRequest request,
        EntryAdministration administration,
        LenderLookup lookup,
        OrderViews views,
        CancellationToken cancellationToken)
    {
        if (request.Version is not { } version)
        {
            return ProblemResults.Invalid(new Dictionary<string, string> { ["version"] = InputFields.Required });
        }

        var result = await administration.UpdateAsync(id, entryId, version, request.ToFields(), cancellationToken);
        if (result.Errors?.GetValueOrDefault("loan.nationalId") == LoanWriter.LenderRegistered)
        {
            await lookup.RecordRegisteredProbeAsync(cancellationToken);
        }

        return await RespondAsync(result, views, cancellationToken);
    }

    /// <summary>The order after a write, or the problem of a failed one.</summary>
    private static async Task<Results<Ok<OrderResponse>, ProblemHttpResult>> RespondAsync(
        OrderResult<ComparsaOrder> result, OrderViews views, CancellationToken cancellationToken)
    {
        if (result is not { Outcome: OrderOutcome.Done, Value: { } order })
        {
            return result.Problem();
        }

        // A committed write the caller cannot read back is a broken invariant, not a missing order.
        return TypedResults.Ok(await views.FindAsync(order.Id, cancellationToken)
            ?? throw new InvalidOperationException($"Order {order.Id} was committed but cannot be read back."));
    }

    private static async Task<Results<Ok<OrderResponse>, ProblemHttpResult>> SubmitAsync(
        Guid id, SubmitOrderRequest request, OrderLifecycle lifecycle, ICurrentUser currentUser, OrderViews views, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        if (request.Version is null)
        {
            errors["version"] = InputFields.Required;
        }

        if (!currentUser.IsAdmin && request.Attestation != true)
        {
            errors["attestation"] = InputFields.Required;
        }

        return errors.Count > 0
            ? ProblemResults.Invalid(errors)
            : await RespondAsync(await lifecycle.SubmitAsync(id, request.Version!.Value, cancellationToken), views, cancellationToken);
    }

    private static async Task<Results<Ok<OrderResponse>, ProblemHttpResult>> ValidateAsync(
        Guid id, ValidateOrderRequest request, OrderLifecycle lifecycle, OrderViews views, CancellationToken cancellationToken) =>
        request.Version is not { } version
            ? ProblemResults.Invalid(new Dictionary<string, string> { ["version"] = InputFields.Required })
            : await RespondAsync(await lifecycle.ValidateAsync(id, version, cancellationToken), views, cancellationToken);

    private static async Task<Results<Ok<OrderResponse>, ProblemHttpResult>> ReturnAsync(
        Guid id, ReturnOrderRequest request, OrderLifecycle lifecycle, OrderViews views, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        if (request.Version is null)
        {
            errors["version"] = InputFields.Required;
        }

        var reason = ReturnReason.Read(request.Reason, errors);
        return errors.Count > 0
            ? ProblemResults.Invalid(errors)
            : await RespondAsync(await lifecycle.ReturnAsync(id, request.Version!.Value, reason!, cancellationToken), views, cancellationToken);
    }

    private static async Task<Results<Ok<LenderLookupResponse>, ProblemHttpResult>> LookUpLenderAsync(
        LenderLookupRequest request, LenderLookup lookup, CancellationToken cancellationToken)
    {
        var result = await lookup.FindAsync(request.NationalId, cancellationToken);
        return result is { Outcome: OrderOutcome.Done, Value: { } lender } ? TypedResults.Ok(lender) : result.Problem();
    }

    private static async Task<Results<Ok<OverviewResponse>, ProblemHttpResult>> OverviewAsync(
        Guid? editionId, OrderOverview overview, CancellationToken cancellationToken) =>
        await overview.FindAsync(editionId, cancellationToken) is { } response
            ? TypedResults.Ok(response)
            : OrderProblems.From(OrderOutcome.NotFound);

    private static async Task<Results<Ok<OrderResponse>, ProblemHttpResult>> GetAsync(Guid id, OrderViews views, CancellationToken cancellationToken) =>
        await views.FindAsync(id, cancellationToken) is { } order
            ? TypedResults.Ok(order)
            : OrderProblems.From(OrderOutcome.NotFound);
}
