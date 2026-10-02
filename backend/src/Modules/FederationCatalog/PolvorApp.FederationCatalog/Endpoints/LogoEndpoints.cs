using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using PolvorApp.FederationCatalog.Logos;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.SharedKernel.Http;
using PolvorApp.SharedKernel.Security;

namespace PolvorApp.FederationCatalog.Endpoints;

/// <summary>
/// Specs "Comparsa logos", "Logo validation and processing" and "Logo access (BR-12)" (design D6):
/// the logo is a sub-resource of a comparsa. Admins write it; anyone who can see the comparsa reads
/// it, streamed by the API after the scope check (the storage is never reachable from the browser).
/// </summary>
internal static class LogoEndpoints
{
    /// <summary>The file plus the multipart framing around it.</summary>
    public const long MaxRequestBytes = LogoStorage.MaxUploadBytes + ImageUploads.FramingBytes;

    /// <summary>A fixed name: the client's file name is never used.</summary>
    private const string InlineDisposition = "inline; filename=\"logo.png\"";

    public static IEndpointRouteBuilder MapLogoEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/comparsas/{id:guid}").WithTags("Comparsas")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        group.MapGet("/logo", GetAsync).WithName("GetComparsaLogo")
            .WithSummary("The comparsa's logo as a PNG image, for a comparsa within the caller's scope; never cached.")
            .Produces<Stream>(StatusCodes.Status200OK, LogoStorage.ContentType);

        var adminOnly = group.MapGroup(string.Empty).RequireAuthorization(AuthorizationPolicies.Admin).ProducesProblem(StatusCodes.Status403Forbidden);
        adminOnly.MapPut("/logo", UploadAsync).WithName("UploadComparsaLogo")
            .WithSummary("Uploads or replaces the comparsa's logo: one JPEG, PNG or WebP image of at most 10 MB, stored as PNG.")
            .Accepts<LogoUploadForm>("multipart/form-data")
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBytes))
            .DisableAntiforgery() // The platform middleware checks X-XSRF-TOKEN before the body is read.
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireRateLimiting(RateLimitPolicies.ImageUploads).ProducesProblem(StatusCodes.Status429TooManyRequests);
        adminOnly.MapDelete("/logo", RemoveAsync).WithName("RemoveComparsaLogo")
            .WithSummary("Removes the comparsa's logo; the stored image is erased.");
        return endpoints;
    }

    /// <summary>
    /// Unknown ids are refused before the body is read, so they cost no upload; the form is then read
    /// in memory by <see cref="ImageUploads"/>.
    /// </summary>
    private static async Task<Results<Ok<ComparsaLogoResponse>, ProblemHttpResult>> UploadAsync(
        Guid id, HttpRequest request, ComparsaLogoAdministration administration, ILoggerFactory loggers, CancellationToken cancellationToken)
    {
        if (!await administration.ExistsAsync(id, cancellationToken))
        {
            return CatalogProblems.From(CatalogOutcome.ComparsaNotFound);
        }

        var (file, problem) = await ImageUploads.ReadFileAsync(
            request, LogoStorage.MaxUploadBytes, loggers.CreateLogger(typeof(LogoEndpoints)), cancellationToken);
        if (file is null)
        {
            return ImageUploads.Invalid(problem!);
        }

        await using var content = file.OpenReadStream();
        var upload = await administration.UploadAsync(id, content, cancellationToken);
        return upload switch
        {
            { Rejection: { } rejection } => ImageUploads.Invalid(ImageUploads.Reason(rejection)),
            { Outcome: CatalogOutcome.Done, Logo: { } logo } => TypedResults.Ok(ComparsaLogoResponse.From(logo)),
            _ => CatalogProblems.From(upload.Outcome),
        };
    }

    private static async Task<IResult> GetAsync(Guid id, HttpContext context, ComparsaLogoAdministration administration, CancellationToken cancellationToken)
    {
        var (outcome, image) = await administration.OpenAsync(id, cancellationToken);
        if (image is null)
        {
            return CatalogProblems.From(outcome);
        }

        context.Response.RegisterForDisposeAsync(image);
        context.Response.ContentLength = image.Length;
        context.Response.Headers.ContentDisposition = InlineDisposition;
        return TypedResults.Stream(image.Content, LogoStorage.ContentType);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> RemoveAsync(
        Guid id, ComparsaLogoAdministration administration, CancellationToken cancellationToken)
    {
        var outcome = await administration.RemoveAsync(id, cancellationToken);
        return outcome == CatalogOutcome.Done ? TypedResults.NoContent() : CatalogProblems.From(outcome);
    }
}

/// <summary>A logo upload: one JPEG, PNG or WebP image of at most 10 MB.</summary>
/// <param name="File">The image, cropped by the client; the server re-encodes it as PNG.</param>
internal sealed record LogoUploadForm(IFormFile File);
