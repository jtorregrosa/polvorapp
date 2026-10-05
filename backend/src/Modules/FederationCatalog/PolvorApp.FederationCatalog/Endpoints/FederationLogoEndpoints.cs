using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using PolvorApp.FederationCatalog.Logos;
using PolvorApp.FederationCatalog.Settings;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.SharedKernel.Http;
using PolvorApp.SharedKernel.Security;

namespace PolvorApp.FederationCatalog.Endpoints;

/// <summary>The Federation's identity and logo as every signed-in user reads them (spec: Federation settings).</summary>
/// <param name="OfficialNameEs">The official name in Spanish.</param>
/// <param name="OfficialNameCa">The official name in Valencian.</param>
/// <param name="ShortName">The short name for the interface.</param>
/// <param name="Logo">The logo printed in documents, or null until an Admin uploads it.</param>
internal sealed record FederationResponse(string OfficialNameEs, string OfficialNameCa, string ShortName, ComparsaLogoResponse? Logo);

/// <summary>
/// Spec "Federation logo" (add-distribution-planning, design D11): Admins upload, replace and remove it;
/// every signed-in user reads it, streamed by the API (the storage is never reachable from the browser)
/// and never cached. Uploads share the comparsa logos' size limit, rules and rate limit.
/// </summary>
internal static class FederationLogoEndpoints
{
    /// <summary>A fixed name: the client's file name is never used.</summary>
    private const string InlineDisposition = "inline; filename=\"federation-logo.png\"";

    public static IEndpointRouteBuilder MapFederationLogoEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(string.Empty).WithTags("Federation")
            .ProducesProblem(StatusCodes.Status401Unauthorized);
        group.MapGet("/federation", FederationAsync).WithName("GetFederation")
            .WithSummary("The Federation's names and whether its logo for documents was uploaded.");
        group.MapGet("/federation-logo", GetAsync).WithName("GetFederationLogo")
            .WithSummary("The Federation's logo as a PNG image; never cached.")
            .Produces<Stream>(StatusCodes.Status200OK, LogoStorage.ContentType)
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        var adminOnly = group.MapGroup(string.Empty).RequireAuthorization(AuthorizationPolicies.Admin).ProducesProblem(StatusCodes.Status403Forbidden);
        adminOnly.MapPut("/federation-logo", UploadAsync).WithName("UploadFederationLogo")
            .WithSummary("Uploads or replaces the Federation's logo for documents: one JPEG, PNG or WebP image of at most 10 MB, stored as PNG.")
            .Accepts<LogoUploadForm>("multipart/form-data")
            .WithMetadata(new RequestSizeLimitAttribute(LogoEndpoints.MaxRequestBytes))
            .DisableAntiforgery() // The platform middleware checks X-XSRF-TOKEN before the body is read.
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .RequireRateLimiting(RateLimitPolicies.ImageUploads).ProducesProblem(StatusCodes.Status429TooManyRequests);
        adminOnly.MapDelete("/federation-logo", RemoveAsync).WithName("RemoveFederationLogo")
            .WithSummary("Removes the Federation's logo; the stored image is erased.")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        return endpoints;
    }

    private static async Task<Ok<FederationResponse>> FederationAsync(FederationSettingsAdministration administration, CancellationToken cancellationToken)
    {
        var settings = await administration.GetAsync(cancellationToken);
        return TypedResults.Ok(new FederationResponse(
            settings.OfficialNameEs, settings.OfficialNameCa, settings.ShortName, settings.Logo is { } logo ? ComparsaLogoResponse.From(logo) : null));
    }

    private static async Task<Results<Ok<ComparsaLogoResponse>, ProblemHttpResult>> UploadAsync(
        HttpRequest request, FederationLogoAdministration administration, ILoggerFactory loggers, CancellationToken cancellationToken)
    {
        var (file, problem) = await ImageUploads.ReadFileAsync(
            request, LogoStorage.MaxUploadBytes, loggers.CreateLogger(typeof(FederationLogoEndpoints)), cancellationToken);
        if (file is null)
        {
            return ImageUploads.Invalid(problem!);
        }

        await using var content = file.OpenReadStream();
        var upload = await administration.UploadAsync(content, cancellationToken);
        return upload switch
        {
            { Rejection: { } rejection } => ImageUploads.Invalid(ImageUploads.Reason(rejection)),
            { Outcome: CatalogOutcome.Done, Logo: { } logo } => TypedResults.Ok(ComparsaLogoResponse.From(logo)),
            _ => CatalogProblems.From(upload.Outcome),
        };
    }

    private static async Task<IResult> GetAsync(HttpContext context, FederationLogoAdministration administration, CancellationToken cancellationToken)
    {
        var (outcome, image) = await administration.OpenAsync(cancellationToken);
        if (image is null)
        {
            return CatalogProblems.From(outcome);
        }

        context.Response.RegisterForDisposeAsync(image);
        context.Response.ContentLength = image.Length;
        context.Response.Headers.ContentDisposition = InlineDisposition;
        return TypedResults.Stream(image.Content, LogoStorage.ContentType);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> RemoveAsync(FederationLogoAdministration administration, CancellationToken cancellationToken)
    {
        var outcome = await administration.RemoveAsync(cancellationToken);
        return outcome == CatalogOutcome.Done ? TypedResults.NoContent() : CatalogProblems.From(outcome);
    }
}
