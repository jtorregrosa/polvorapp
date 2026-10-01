using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ArquebusierRegistry.Photos;
using PolvorApp.SharedKernel.Http;
using PolvorApp.SharedKernel.Images;
using PolvorApp.SharedKernel.Security;
using PolvorApp.SharedKernel.Validation;

namespace PolvorApp.ArquebusierRegistry.Endpoints;

/// <summary>
/// Specs "Arquebusier photos", "Photo validation and processing" and "Private photo access"
/// (design D5): photos are a sub-resource of an arquebusier in the caller's scope. The API streams
/// them after the scope check; the storage is never reachable from the browser (SEC-02).
/// </summary>
internal static partial class PhotoEndpoints
{
    /// <summary>The file plus the multipart framing around it.</summary>
    public const long MaxRequestBytes = PhotoStorage.MaxUploadBytes + (64 * 1024);

    private const string FileField = "file";

    /// <summary>
    /// One file, all in memory: the default would spill parts over 64 KB to a temporary file, and a
    /// photo of a person must never be written to the server's disk unencrypted.
    /// </summary>
    private static readonly FormOptions Form = new()
    {
        MultipartBodyLengthLimit = PhotoStorage.MaxUploadBytes,
        MemoryBufferThreshold = (int)MaxRequestBytes,
        ValueCountLimit = 4,
    };

    /// <summary>A fixed name: the client's file name may contain personal data and is never used.</summary>
    private const string InlineDisposition = "inline; filename=\"photo.jpg\"";

    public static IEndpointRouteBuilder MapPhotoEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // An unknown kind does not match the route: 404 like any unknown path. Route regexes ignore
        // case, so the handlers accept only the lower-case slugs and answer 404 otherwise.
        // Reads and removals take no body: the registry's 64 KB limit; only the upload takes more.
        var group = endpoints.MapGroup("/arquebusiers/{id:guid}/photos/{kind:regex(^(id|license-front|license-back)$)}").WithTags("Arquebusiers")
            .WithMetadata(new RequestSizeLimitAttribute(ArquebusierEndpoints.MaxBodyBytes))
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        group.MapPut("/", UploadAsync).WithName("UploadArquebusierPhoto")
            .WithSummary("Uploads or replaces a photo (id, license-front, license-back) of an arquebusier of the caller's comparsas.")
            .Accepts<PhotoUploadForm>("multipart/form-data")
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBytes))
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status409Conflict)
            .RequireRateLimiting(RateLimitPolicies.PersonalDataWrites).ProducesProblem(StatusCodes.Status429TooManyRequests);
        group.MapGet("/", GetAsync).WithName("GetArquebusierPhoto")
            .WithSummary("The photo as a JPEG image; never cached.")
            .Produces<Stream>(StatusCodes.Status200OK, PhotoStorage.ContentType);
        group.MapDelete("/", RemoveAsync).WithName("RemoveArquebusierPhoto")
            .WithSummary("Removes a photo; the stored image is erased.")
            .RequireRateLimiting(RateLimitPolicies.PersonalDataWrites).ProducesProblem(StatusCodes.Status429TooManyRequests);
        return endpoints;
    }

    /// <summary>
    /// The scope is checked before the body is read, so out-of-scope and unknown ids cost no upload
    /// (spec: Arquebusier photos). The form is then read here rather than bound, entirely in memory (a
    /// personal photo never touches the server's disk), and an oversized or malformed body becomes the
    /// same field error as any other invalid file. The platform middleware has already checked the
    /// anti-forgery header, before the body is read.
    /// </summary>
    private static async Task<Results<Ok<ArquebusierPhotoResponse>, ProblemHttpResult>> UploadAsync(
        Guid id, string kind, HttpRequest request, ArquebusierPhotoAdministration administration, ILoggerFactory loggers, CancellationToken cancellationToken)
    {
        if (!PhotoStorage.TryParseSlug(kind, out var photoKind))
        {
            return ProblemResults.NotFound(RegistryProblems.PhotoNotFound);
        }

        if (!await administration.ExistsInScopeAsync(id, photoKind, cancellationToken))
        {
            return RegistryProblems.From(RegistryOutcome.ArquebusierNotFound);
        }

        var (file, problem) = await ReadFileAsync(request, loggers.CreateLogger(typeof(PhotoEndpoints)), cancellationToken);
        if (file is null)
        {
            return Invalid(problem!);
        }

        await using var content = file.OpenReadStream();
        var upload = await administration.UploadAsync(id, photoKind, content, cancellationToken);
        return upload switch
        {
            { Rejection: { } rejection } => Invalid(Reason(rejection)),
            { Outcome: RegistryOutcome.Done, Photo: { } photo } => TypedResults.Ok(ArquebusierPhotoResponse.From(photo)),
            _ => RegistryProblems.From(upload.Outcome),
        };
    }

    /// <summary>The <c>file</c> part of a <c>multipart/form-data</c> body, or the field reason why there is none.</summary>
    private static async Task<(IFormFile? File, string? Problem)> ReadFileAsync(HttpRequest request, ILogger logger, CancellationToken cancellationToken)
    {
        if (!(request.ContentType ?? string.Empty).StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase))
        {
            return (null, InputFields.Required);
        }

        try
        {
            var form = await request.ReadFormAsync(Form, cancellationToken);
            return form.Files.GetFile(FileField) is { } file ? (file, null) : (null, InputFields.Required);
        }
        catch (BadHttpRequestException exception) when (exception.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return (null, Reason(ImageRejection.TooLarge));
        }
        catch (Exception exception) when (exception is InvalidDataException or BadHttpRequestException)
        {
            // A part over the limit, or a malformed body: no readable file either way.
            var tooLarge = request.ContentLength > PhotoStorage.MaxUploadBytes || exception.Message.Contains("length limit", StringComparison.OrdinalIgnoreCase);
            var failure = exception.GetType().Name;
            LogUnreadableForm(logger, tooLarge, failure);
            return (null, tooLarge ? Reason(ImageRejection.TooLarge) : InputFields.Required);
        }
    }

    private static async Task<IResult> GetAsync(
        Guid id, string kind, HttpContext context, ArquebusierPhotoAdministration administration, CancellationToken cancellationToken)
    {
        if (!PhotoStorage.TryParseSlug(kind, out var photoKind))
        {
            return ProblemResults.NotFound(RegistryProblems.PhotoNotFound);
        }

        var (outcome, image) = await administration.OpenAsync(id, photoKind, cancellationToken);
        if (image is null)
        {
            return RegistryProblems.From(outcome);
        }

        context.Response.RegisterForDisposeAsync(image);
        context.Response.ContentLength = image.Length;
        context.Response.Headers.ContentDisposition = InlineDisposition;
        return TypedResults.Stream(image.Content, PhotoStorage.ContentType);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> RemoveAsync(
        Guid id, string kind, ArquebusierPhotoAdministration administration, CancellationToken cancellationToken)
    {
        if (!PhotoStorage.TryParseSlug(kind, out var photoKind))
        {
            return ProblemResults.NotFound(RegistryProblems.PhotoNotFound);
        }

        var outcome = await administration.RemoveAsync(id, photoKind, cancellationToken);
        return outcome == RegistryOutcome.Done ? TypedResults.NoContent() : RegistryProblems.From(outcome);
    }

    private static ProblemHttpResult Invalid(string reason) =>
        ProblemResults.Invalid(new Dictionary<string, string> { [FileField] = reason });

    [LoggerMessage(Level = LogLevel.Information, Message = "Photo upload body could not be read (too large: {TooLarge}, {Failure})")]
    private static partial void LogUnreadableForm(ILogger logger, bool tooLarge, string failure);

    /// <summary>Field reasons the UI translates as <c>registry:validation.file.&lt;reason&gt;</c>.</summary>
    private static string Reason(ImageRejection rejection) => rejection switch
    {
        ImageRejection.TooLarge => "tooLarge",
        ImageRejection.UnsupportedFormat => "unsupportedFormat",
        ImageRejection.TooSmall => "tooSmall",
        ImageRejection.AspectRatio => "aspectRatio",
        _ => throw new ArgumentOutOfRangeException(nameof(rejection), rejection, "Unknown image rejection."),
    };
}

/// <summary>A photo upload: one JPEG, PNG or WebP image of at most 10 MB.</summary>
/// <param name="File">The image, cropped by the client; the server re-encodes it.</param>
internal sealed record PhotoUploadForm(IFormFile File);
