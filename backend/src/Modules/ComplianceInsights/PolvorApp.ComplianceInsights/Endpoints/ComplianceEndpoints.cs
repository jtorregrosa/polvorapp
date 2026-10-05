using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.SharedKernel.Http;
using PolvorApp.SharedKernel.Validation;

namespace PolvorApp.ComplianceInsights.Endpoints;

/// <summary>
/// Specs "Warning summary", "Statistics (UC-07)" (design D4) and "Edition trends (UC-07)": read-only insights of the caller's
/// comparsas. Every signed-in user may call them; the scope comes from the comparsa assignments
/// (BR-12). Nothing is written or exported, so nothing is audited.
/// </summary>
internal static class ComplianceEndpoints
{
    public static IEndpointRouteBuilder MapComplianceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/compliance").WithTags("Compliance")
            .ProducesProblem(StatusCodes.Status401Unauthorized);
        group.MapGet("/summary", SummaryAsync).WithName("GetComplianceSummary")
            .WithSummary("Active and reserve arquebusiers of the caller's comparsas and how many have each compliance warning.");
        group.MapGet("/statistics", StatisticsAsync).WithName("GetComplianceStatistics")
            .WithSummary("Aggregate statistics and equality report of the caller's comparsas, optionally of one comparsa and one status.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);
        group.MapGet("/trends", TrendsAsync).WithName("GetComplianceTrends")
            .WithSummary("Participation counts of the caller's comparsas in the 10 most recent started editions, optionally of one comparsa.")
            .ProducesProblem(StatusCodes.Status404NotFound);
        return endpoints;
    }

    /// <summary>Spec "Edition trends (UC-07)"; a comparsa outside the scope and an unknown one look the same (BR-12).</summary>
    private static async Task<Results<Ok<TrendsResponse>, ProblemHttpResult>> TrendsAsync(
        Guid? comparsaId, TrendQueries queries, CancellationToken cancellationToken) =>
        await queries.TrendsAsync(comparsaId, cancellationToken) is { } trends
            ? TypedResults.Ok(trends)
            : ComplianceProblems.ComparsaMissing();

    private static async Task<Ok<ComplianceSummaryResponse>> SummaryAsync(ComplianceInsightsQueries queries, CancellationToken cancellationToken) =>
        TypedResults.Ok(await queries.SummaryAsync(cancellationToken));

    /// <summary>A comparsa outside the scope and an unknown one look the same (BR-12).</summary>
    private static async Task<Results<Ok<ComplianceStatisticsResponse>, ProblemHttpResult>> StatisticsAsync(
        Guid? comparsaId, string? status, ComplianceInsightsQueries queries, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        var statusFilter = InputFields.OptionalCode<ArquebusierStatus>(status, "status", errors);
        if (errors.Count > 0)
        {
            return ProblemResults.Invalid(errors);
        }

        return await queries.StatisticsAsync(comparsaId, statusFilter, cancellationToken) is { } statistics
            ? TypedResults.Ok(statistics)
            : ComplianceProblems.ComparsaMissing();
    }
}
