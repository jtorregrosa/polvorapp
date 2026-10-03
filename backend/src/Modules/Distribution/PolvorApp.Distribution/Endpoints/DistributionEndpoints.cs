using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PolvorApp.Distribution.Contracts;
using PolvorApp.Distribution.Days;
using PolvorApp.Distribution.Documents;
using PolvorApp.Distribution.Proxies;
using PolvorApp.Exports.Contracts;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.SharedKernel.Http;
using PolvorApp.SharedKernel.Security;
using PolvorApp.SharedKernel.Validation;

namespace PolvorApp.Distribution.Endpoints;

/// <summary>
/// Specs "Distribution days", "Distribution slots" and "Distribution visibility" (design D7): every
/// signed-in user reads an edition's plan, scoped to their comparsas; Admins plan, edit and delete days
/// and save their slots.
/// </summary>
internal static class DistributionEndpoints
{
    /// <summary>A day or a full set of slots for ~20 comparsas is a few KB.</summary>
    public const long MaxRequestBytes = 64 * 1024;

    /// <summary>More slots than any Federation has comparsas.</summary>
    public const int MaxSlots = 200;

    public static IEndpointRouteBuilder MapDistributionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/distribution").WithTags("Distribution")
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBytes))
            .ProducesProblem(StatusCodes.Status401Unauthorized).ProducesProblem(StatusCodes.Status404NotFound);
        group.MapGet("/editions/{editionId:guid}", PlanAsync).WithName("GetDistributionPlan")
            .WithSummary("The edition's distribution days with their slots: every slot for Admins, the user's comparsas' for FiringChiefs.");

        var admins = group.MapGroup(string.Empty).RequireAuthorization(AuthorizationPolicies.Admin)
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        admins.MapPost("/editions/{editionId:guid}/distributions", PlanDayAsync).WithName("PlanDistribution")
            .WithSummary("Plans the powder or weapons day of the edition in progress (Admins).")
            .ProducesProblem(StatusCodes.Status400BadRequest);
        admins.MapPut("/distributions/{id:guid}", EditDayAsync).WithName("EditDistribution")
            .WithSummary("Changes a day's date or location (Admins).")
            .ProducesProblem(StatusCodes.Status400BadRequest);
        admins.MapDelete("/distributions/{id:guid}", DeleteDayAsync).WithName("DeleteDistribution")
            .WithSummary("Deletes a day with its slots; pickup proxies are kept (Admins).");
        admins.MapPut("/distributions/{id:guid}/slots", SaveSlotsAsync).WithName("SaveDistributionSlots")
            .WithSummary("Replaces a day's slots with the given set (Admins).")
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/editions/{editionId:guid}/proxies", ListProxiesAsync).WithName("ListPickupProxies")
            .WithSummary("The edition's pickup proxies the user may see, optionally of one comparsa, with their problems.");
        group.MapGet("/editions/{editionId:guid}/comparsas/{comparsaId:guid}/proxy-candidates", ProxyCandidatesAsync).WithName("ListProxyCandidates")
            .WithSummary("The entries of the comparsa's order with what each may hold or collect, for the add-proxy panel.");
        group.MapPost("/editions/{editionId:guid}/proxies", RegisterProxyAsync).WithName("RegisterPickupProxy")
            .WithSummary("Registers a pickup proxy (FiringChiefs of the comparsa while the edition is in progress; Admins).")
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        group.MapDelete("/proxies/{id:guid}", RemoveProxyAsync).WithName("RemovePickupProxy")
            .WithSummary("Removes a pickup proxy (FiringChiefs of the comparsa while the edition is in progress; Admins).")
            .ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        // Documents: generated per request, audited before they are returned, limited per user like the exports.
        group.MapGet("/distributions/{id:guid}/list/{format}", ListDocumentAsync).WithName("DownloadDistributionList")
            .WithSummary("A day's distribution list from the validated orders, as xlsx or pdf, in the user's language; audited (Admins).")
            .RequireAuthorization(AuthorizationPolicies.Admin).ProducesProblem(StatusCodes.Status403Forbidden)
            .RequireRateLimiting(RateLimitPolicies.Exports).ProducesProblem(StatusCodes.Status429TooManyRequests)
            .Produces<Stream>(StatusCodes.Status200OK, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "application/pdf")
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        group.MapGet("/proxies/{id:guid}/form", FormDocumentAsync).WithName("DownloadPickupAuthorisation")
            .WithSummary("A proxy's pre-filled authorisation form as pdf, in the user's language; audited (Admins and the comparsa's FiringChiefs).")
            .RequireRateLimiting(RateLimitPolicies.Exports).ProducesProblem(StatusCodes.Status429TooManyRequests)
            .Produces<Stream>(StatusCodes.Status200OK, "application/pdf")
            .ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        return endpoints;
    }

    private static Task<Results<FileContentHttpResult, ProblemHttpResult>> ListDocumentAsync(
        Guid id, string format, DistributionDocuments documents, CancellationToken cancellationToken) =>
        format switch
        {
            "xlsx" => documents.ListAsync(id, DocumentFileFormat.Xlsx, cancellationToken),
            "pdf" => documents.ListAsync(id, DocumentFileFormat.Pdf, cancellationToken),
            _ => Task.FromResult<Results<FileContentHttpResult, ProblemHttpResult>>(DistributionProblems.From(DistributionOutcome.NotFound)),
        };

    private static Task<Results<FileContentHttpResult, ProblemHttpResult>> FormDocumentAsync(
        Guid id, DistributionDocuments documents, CancellationToken cancellationToken) =>
        documents.FormAsync(id, cancellationToken);

    private static async Task<Results<Ok<IReadOnlyList<ProxyResponse>>, ProblemHttpResult>> ListProxiesAsync(
        Guid editionId, Guid? comparsaId, ProxyViews views, CancellationToken cancellationToken) =>
        await views.ListAsync(editionId, comparsaId, cancellationToken) is { } proxies
            ? TypedResults.Ok(proxies)
            : DistributionProblems.From(DistributionOutcome.NotFound);

    private static async Task<Results<Ok<IReadOnlyList<ProxyCandidateResponse>>, ProblemHttpResult>> ProxyCandidatesAsync(
        Guid editionId, Guid comparsaId, ProxyViews views, CancellationToken cancellationToken) =>
        await views.CandidatesAsync(editionId, comparsaId, cancellationToken) is { } candidates
            ? TypedResults.Ok(candidates)
            : DistributionProblems.From(DistributionOutcome.NotFound);

    private static async Task<Results<Created<ProxyResponse>, ProblemHttpResult>> RegisterProxyAsync(
        Guid editionId, ProxyRequest request, ProxyAdministration administration, ProxyViews views, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        var type = InputFields.RequiredCode<DistributionType>(request.Type, "type", errors);
        if (request.HolderEntryId is null)
        {
            errors["holderEntryId"] = InputFields.Required;
        }

        if (request.ProxyEntryId is null)
        {
            errors["proxyEntryId"] = InputFields.Required;
        }

        if (errors.Count > 0 || type is not { } validType)
        {
            return ProblemResults.Invalid(errors);
        }

        var result = await administration.RegisterAsync(editionId, request.HolderEntryId!.Value, request.ProxyEntryId!.Value, validType, cancellationToken);
        if (result is not { Outcome: DistributionOutcome.Done, Value: { } proxy })
        {
            return result.Problem();
        }

        // Gone right after the commit (its entry was deleted with its arquebusier): as if never registered.
        return await views.CreatedAsync(proxy, cancellationToken) is { } created
            ? TypedResults.Created($"/api/distribution/proxies/{proxy.Id}", created)
            : DistributionProblems.From(DistributionOutcome.ProxyNotFound);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> RemoveProxyAsync(Guid id, ProxyAdministration administration, CancellationToken cancellationToken)
    {
        var result = await administration.RemoveAsync(id, cancellationToken);
        return result.Outcome == DistributionOutcome.Done ? TypedResults.NoContent() : result.Problem();
    }

    private static async Task<Results<Ok<DistributionPlanResponse>, ProblemHttpResult>> PlanAsync(
        Guid editionId, DistributionViews views, CancellationToken cancellationToken) =>
        await views.PlanAsync(editionId, cancellationToken) is { } plan
            ? TypedResults.Ok(plan)
            : DistributionProblems.From(DistributionOutcome.NotFound);

    private static async Task<Results<Created<DistributionDayResponse>, ProblemHttpResult>> PlanDayAsync(
        Guid editionId, PlanDayRequest request, DistributionDayAdministration administration, DistributionViews views, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        var type = InputFields.RequiredCode<DistributionType>(request.Type, "type", errors);
        if (!TryReadDay(request.Date, request.Location, errors, out var input) || type is not { } validType)
        {
            return ProblemResults.Invalid(errors);
        }

        var result = await administration.PlanAsync(editionId, validType, input, cancellationToken);
        return result is { Outcome: DistributionOutcome.Done, Value: { } day }
            ? TypedResults.Created($"/api/distribution/distributions/{day.Id}", await views.AdminDayAsync(day, cancellationToken))
            : result.Problem();
    }

    private static async Task<Results<Ok<DistributionDayResponse>, ProblemHttpResult>> EditDayAsync(
        Guid id, EditDayRequest request, DistributionDayAdministration administration, DistributionViews views, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        var readDay = TryReadDay(request.Date, request.Location, errors, out var input);
        if (request.Version is null)
        {
            errors["version"] = InputFields.Required;
        }

        if (!readDay || errors.Count > 0)
        {
            return ProblemResults.Invalid(errors);
        }

        var result = await administration.EditAsync(id, input!, request.Version!.Value, cancellationToken);
        return result is { Outcome: DistributionOutcome.Done, Value: { } day } ? TypedResults.Ok(await views.AdminDayAsync(day, cancellationToken)) : result.Problem();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteDayAsync(
        Guid id, uint version, DistributionDayAdministration administration, CancellationToken cancellationToken)
    {
        var result = await administration.DeleteAsync(id, version, cancellationToken);
        return result.Outcome == DistributionOutcome.Done ? TypedResults.NoContent() : result.Problem();
    }

    private static async Task<Results<Ok<DistributionDayResponse>, ProblemHttpResult>> SaveSlotsAsync(
        Guid id, SaveSlotsRequest request, DistributionDayAdministration administration, DistributionViews views, CancellationToken cancellationToken)
    {
        if (request.Slots is null || request.Version is null)
        {
            var missing = new Dictionary<string, string>(StringComparer.Ordinal);
            if (request.Slots is null)
            {
                missing["slots"] = InputFields.Required;
            }

            if (request.Version is null)
            {
                missing["version"] = InputFields.Required;
            }

            return ProblemResults.Invalid(missing);
        }

        if (!TryReadSlots(request.Slots, out var slots, out var errors))
        {
            return ProblemResults.Invalid(errors);
        }

        var result = await administration.SaveSlotsAsync(id, request.Version.Value, slots, cancellationToken);
        return result is { Outcome: DistributionOutcome.Done, Value: { } day } ? TypedResults.Ok(await views.AdminDayAsync(day, cancellationToken)) : result.Problem();
    }

    private static bool TryReadDay(string? date, string? location, Dictionary<string, string> errors, [NotNullWhen(true)] out DayInput? input)
    {
        var parsedDate = InputFields.RequiredDate(date, "date", errors);
        var parsedLocation = InputFields.Text(location, "location", DistributionDay.LocationMaxLength, errors);
        input = parsedDate is { } validDate && parsedLocation is not null && errors.Count == 0 ? new DayInput(validDate, parsedLocation) : null;
        return input is not null;
    }

    /// <summary>Each slot names a comparsa once, with a time from 00:00 to 23:59; errors are named by index.</summary>
    private static bool TryReadSlots(IReadOnlyList<SlotRequest> requests, out IReadOnlyList<SlotInput> slots, out Dictionary<string, string> errors)
    {
        errors = new Dictionary<string, string>(StringComparer.Ordinal);
        var read = new List<SlotInput>(requests.Count);
        var seen = new HashSet<Guid>();
        slots = read;
        if (requests.Count > MaxSlots)
        {
            errors["slots"] = InputFields.TooLong;
            return false;
        }

        for (var i = 0; i < requests.Count; i++)
        {
            var request = requests[i];
            if (request?.ComparsaId is not { } comparsaId)
            {
                errors[$"slots[{i}].comparsaId"] = InputFields.Required;
            }
            else if (!seen.Add(comparsaId))
            {
                errors[$"slots[{i}].comparsaId"] = DistributionFieldErrors.Duplicate;
            }

            if (!TimeOnly.TryParseExact(request?.StartsAt, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var startsAt))
            {
                errors[$"slots[{i}].startsAt"] = string.IsNullOrEmpty(request?.StartsAt) ? InputFields.Required : InputFields.Invalid;
            }
            else if (request?.ComparsaId is { } id)
            {
                read.Add(new SlotInput(id, startsAt));
            }
        }

        return errors.Count == 0;
    }
}
