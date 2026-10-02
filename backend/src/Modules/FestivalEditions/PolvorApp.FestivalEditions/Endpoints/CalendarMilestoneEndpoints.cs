using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PolvorApp.FestivalEditions.Editions;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.SharedKernel.Http;
using PolvorApp.SharedKernel.Validation;

namespace PolvorApp.FestivalEditions.Endpoints;

/// <summary>Spec "Calendar milestones": Admins add, edit and remove the milestones of an edition.</summary>
internal static class CalendarMilestoneEndpoints
{
    public static IEndpointRouteBuilder MapCalendarMilestoneEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/editions/{id:guid}/milestones").WithTags("Editions")
            .RequireAuthorization(AuthorizationPolicies.Admin)
            .WithMetadata(new RequestSizeLimitAttribute(EditionEndpoints.MaxRequestBytes))
            .ProducesProblem(StatusCodes.Status401Unauthorized).ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        group.MapPost("/", AddAsync).WithName("AddCalendarMilestone").WithSummary("Adds a milestone to an edition.")
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status409Conflict);
        group.MapPut("/{milestoneId:guid}", UpdateAsync).WithName("UpdateCalendarMilestone").WithSummary("Changes a milestone.")
            .ProducesProblem(StatusCodes.Status400BadRequest);
        group.MapDelete("/{milestoneId:guid}", RemoveAsync).WithName("RemoveCalendarMilestone").WithSummary("Removes a milestone.");
        return endpoints;
    }

    private static async Task<Results<Created<CalendarMilestoneResponse>, ProblemHttpResult>> AddAsync(
        Guid id, CalendarMilestoneRequest request, CalendarMilestoneAdministration administration, CancellationToken cancellationToken)
    {
        if (!TryRead(request, out var input, out var invalid))
        {
            return invalid;
        }

        var result = await administration.AddAsync(id, input, cancellationToken);
        return result is { Outcome: EditionOutcome.Done, Milestone: { } milestone }
            ? TypedResults.Created($"/api/editions/{id}/milestones/{milestone.Id}", Response(milestone))
            : EditionProblems.From(result);
    }

    private static async Task<Results<Ok<CalendarMilestoneResponse>, ProblemHttpResult>> UpdateAsync(
        Guid id, Guid milestoneId, CalendarMilestoneRequest request, CalendarMilestoneAdministration administration, CancellationToken cancellationToken)
    {
        if (!TryRead(request, out var input, out var invalid))
        {
            return invalid;
        }

        var result = await administration.UpdateAsync(id, milestoneId, input, cancellationToken);
        return result is { Outcome: EditionOutcome.Done, Milestone: { } milestone }
            ? TypedResults.Ok(Response(milestone))
            : EditionProblems.From(result);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> RemoveAsync(
        Guid id, Guid milestoneId, CalendarMilestoneAdministration administration, CancellationToken cancellationToken)
    {
        var result = await administration.RemoveAsync(id, milestoneId, cancellationToken);
        return result.Outcome == EditionOutcome.Done ? TypedResults.NoContent() : EditionProblems.From(result);
    }

    private static CalendarMilestoneResponse Response(CalendarMilestone milestone) => new(milestone.Id, milestone.Date, milestone.Title);

    private static bool TryRead(
        CalendarMilestoneRequest request, [NotNullWhen(true)] out CalendarMilestoneInput? input, [NotNullWhen(false)] out ProblemHttpResult? invalid)
    {
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        var date = InputFields.RequiredDate(request.Date, "date", errors);
        var title = InputFields.Text(request.Title, "title", CalendarMilestone.TitleMaxLength, errors);
        if (date is { } validDate && title is not null && errors.Count == 0)
        {
            (input, invalid) = (new CalendarMilestoneInput(validDate, title), null);
            return true;
        }

        (input, invalid) = (null, ProblemResults.Invalid(errors));
        return false;
    }
}
