using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using PolvorApp.Distribution.Handovers;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.SharedKernel.Http;
using PolvorApp.SharedKernel.Security;
using PolvorApp.SharedKernel.Validation;

namespace PolvorApp.Distribution.Endpoints;

/// <summary>A batch of handovers captured on a device (spec: Handover sync and conflicts).</summary>
internal sealed record HandoverSyncRequest(IReadOnlyList<HandoverItemRequest?>? Handovers);

/// <summary>One captured handover; the device generates its id.</summary>
internal sealed record HandoverItemRequest(
    Guid? Id,
    Guid? HolderEntryId,
    int? DistributionNumber,
    string? CollectedBy,
    Guid? CollectorEntryId,
    string? RentalFlaskNumber,
    string? Traceability1,
    string? Traceability2,
    DateTimeOffset? CollectedAt);

/// <summary>Each handover's outcome, in the order sent.</summary>
internal sealed record HandoverSyncResponse(IReadOnlyList<SyncResult> Results);

/// <summary>
/// Specs "Offline capture package", "Powder handovers" and "Handover sync and conflicts" (UC-21;
/// add-offline-distribution-capture D2): Admins only.
/// </summary>
internal static class HandoverEndpoints
{
    public static RouteGroupBuilder MapHandoverEndpoints(this RouteGroupBuilder group)
    {
        var admins = group.MapGroup(string.Empty).RequireAuthorization(AuthorizationPolicies.Admin)
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        admins.MapGet("/distributions/{id:guid}/capture", PackageAsync).WithName("GetCapturePackage")
            .WithSummary("The powder day's holders and handovers for offline capture; audited (Admins).")
            .RequireRateLimiting(RateLimitPolicies.Exports).ProducesProblem(StatusCodes.Status429TooManyRequests);
        admins.MapPost("/distributions/{id:guid}/handovers/sync", SyncAsync).WithName("SyncHandovers")
            .WithSummary("Records up to 100 handovers captured on a device, each on its own, with its outcome (Admins).")
            .RequireRateLimiting(RateLimitPolicies.HandoverSync).ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status400BadRequest);
        admins.MapDelete("/handovers/{id:guid}", UndoAsync).WithName("UndoHandover")
            .WithSummary("Undoes a recorded handover with its version, freeing the holder and the flask number; audited (Admins).");
        return group;
    }

    private static Task<Results<Ok<CapturePackageResponse>, ProblemHttpResult>> PackageAsync(Guid id, HandoverCapture capture, CancellationToken cancellationToken) =>
        capture.PackageAsync(id, cancellationToken);

    private static async Task<Results<Ok<HandoverSyncResponse>, ProblemHttpResult>> SyncAsync(
        Guid id, HandoverSyncRequest request, HandoverSync sync, CancellationToken cancellationToken)
    {
        if (!TryRead(request, out var items, out var errors))
        {
            return ProblemResults.Invalid(errors);
        }

        var result = await sync.SyncAsync(id, items, cancellationToken);
        return result is { Outcome: DistributionOutcome.Done, Value: { } results } ? TypedResults.Ok(new HandoverSyncResponse(results)) : result.Problem();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> UndoAsync(Guid id, uint version, HandoverAdministration administration, CancellationToken cancellationToken)
    {
        var result = await administration.UndoAsync(id, version, cancellationToken);
        return result.Outcome == DistributionOutcome.Done ? TypedResults.NoContent() : result.Problem();
    }

    /// <summary>
    /// The batch's shape: 1 to 100 handovers, each with its id (errors named by index). A handover with an
    /// id but other fields missing or unknown is read as unreadable and refused on its own (design D3), so
    /// one bad handover never blocks a device's queue.
    /// </summary>
    private static bool TryRead(HandoverSyncRequest request, out IReadOnlyList<SyncItem> items, out Dictionary<string, string> errors)
    {
        errors = new Dictionary<string, string>(StringComparer.Ordinal);
        var read = new List<SyncItem>();
        items = read;
        if (request.Handovers is not { Count: > 0 and <= HandoverSync.MaxBatch } handovers)
        {
            errors["handovers"] = request.Handovers is null or { Count: 0 } ? InputFields.Required : InputFields.TooLong;
            return false;
        }

        for (var i = 0; i < handovers.Count; i++)
        {
            if (handovers[i] is not { Id: { } itemId } item)
            {
                errors[$"handovers[{i}].id"] = InputFields.Required;
                continue;
            }

            var collectedBy = InputFields.RequiredCode<HandoverCollector>(item.CollectedBy, "collectedBy", new Dictionary<string, string>(StringComparer.Ordinal));
            read.Add(item is { HolderEntryId: { } holder, DistributionNumber: { } number, CollectedAt: { } collectedAt } && collectedBy is { } by
                ? new SyncItem(itemId, new HandoverInput(itemId, holder, number, by, item.CollectorEntryId, item.RentalFlaskNumber, item.Traceability1, item.Traceability2, collectedAt))
                : new SyncItem(itemId, null));
        }

        return errors.Count == 0;
    }
}
