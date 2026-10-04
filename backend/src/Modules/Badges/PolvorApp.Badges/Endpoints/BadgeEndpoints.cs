using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PolvorApp.Badges.Batches;
using PolvorApp.Badges.Documents;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.SharedKernel.Security;

namespace PolvorApp.Badges.Endpoints;

/// <summary>
/// Spec "Badge access and document handling" (design D6): Admins download a badge sheet for a comparsa
/// or a selection. A POST because a selection of 200 ids does not fit a URL; it changes nothing.
/// </summary>
internal static class BadgeEndpoints
{
    /// <summary>200 ids and a language are under 8 KB.</summary>
    public const long MaxRequestBytes = 16 * 1024;

    public static IEndpointRouteBuilder MapBadgeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGroup("/badges").WithTags("Badges")
            .RequireAuthorization(AuthorizationPolicies.Admin)
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBytes))
            .MapPost("/sheet", SheetAsync).WithName("DownloadBadgeSheet")
            .WithSummary("Arquebusier badges of a comparsa or a selection as a pdf to print at 100 % and cut, in the chosen language; audited (Admins).")
            .RequireRateLimiting(RateLimitPolicies.Exports)
            .Produces<Stream>(StatusCodes.Status200OK, "application/pdf")
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        return endpoints;
    }

    private static Task<Results<FileContentHttpResult, ProblemHttpResult>> SheetAsync(
        BadgeSheetRequest request, BadgeDocuments documents, CancellationToken cancellationToken) =>
        documents.SheetAsync(request, cancellationToken);
}
