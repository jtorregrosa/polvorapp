using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging;
using PolvorApp.SharedKernel.Images;
using PolvorApp.SharedKernel.Validation;

namespace PolvorApp.SharedKernel.Http;

/// <summary>
/// Reads an image upload: one <c>file</c> part of a <c>multipart/form-data</c> body
/// (add-arquebusier-photos design D5; add-comparsa-logos design D2). The form is read here rather
/// than bound, entirely in memory (an uploaded image never touches the server's disk), and an
/// oversized or malformed body becomes the same field error as any other invalid file. The platform
/// middleware has already checked the anti-forgery header before the body is read.
/// </summary>
public static partial class ImageUploads
{
    /// <summary>The form field that carries the image.</summary>
    public const string FileField = "file";

    /// <summary>Room for the multipart framing around the file.</summary>
    public const long FramingBytes = 64 * 1024;

    /// <summary>The request size limit for an upload of at most <paramref name="maxFileBytes"/>.</summary>
    public static long MaxRequestBytes(long maxFileBytes) => maxFileBytes + FramingBytes;

    /// <summary>
    /// The <c>file</c> part, or the field reason (<c>required</c>, <c>tooLarge</c>) why there is none.
    /// The endpoint must limit its request size to <see cref="MaxRequestBytes"/>, so the whole body
    /// fits the in-memory buffer.
    /// </summary>
    public static async Task<(IFormFile? File, string? Problem)> ReadFileAsync(
        HttpRequest request, long maxFileBytes, ILogger logger, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFileBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaxRequestBytes(maxFileBytes), int.MaxValue, nameof(maxFileBytes));
        if (!(request.ContentType ?? string.Empty).StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase))
        {
            return (null, InputFields.Required);
        }

        var options = new FormOptions
        {
            MultipartBodyLengthLimit = maxFileBytes,
            MemoryBufferThreshold = checked((int)MaxRequestBytes(maxFileBytes)),
            ValueCountLimit = 4,
        };
        try
        {
            var form = await request.ReadFormAsync(options, cancellationToken);
            return form.Files.GetFile(FileField) is { } file ? (file, null) : (null, InputFields.Required);
        }
        catch (BadHttpRequestException exception) when (exception.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return (null, Reason(ImageRejection.TooLarge));
        }
        catch (Exception exception) when (exception is InvalidDataException or BadHttpRequestException)
        {
            // A part over the limit, or a malformed body: no readable file either way.
            var tooLarge = request.ContentLength > maxFileBytes || exception.Message.Contains("length limit", StringComparison.OrdinalIgnoreCase);
            var failure = exception.GetType().Name;
            LogUnreadableForm(logger, tooLarge, failure);
            return (null, tooLarge ? Reason(ImageRejection.TooLarge) : InputFields.Required);
        }
    }

    /// <summary>The field reason the UI translates for a rejected image.</summary>
    public static string Reason(ImageRejection rejection) => rejection switch
    {
        ImageRejection.TooLarge => "tooLarge",
        ImageRejection.UnsupportedFormat => "unsupportedFormat",
        ImageRejection.TooSmall => "tooSmall",
        ImageRejection.AspectRatio => "aspectRatio",
        _ => throw new ArgumentOutOfRangeException(nameof(rejection), rejection, "Unknown image rejection."),
    };

    /// <summary>A <c>400</c> naming the <c>file</c> field with <paramref name="reason"/>.</summary>
    public static ProblemHttpResult Invalid(string reason) =>
        ProblemResults.Invalid(new Dictionary<string, string> { [FileField] = reason });

    [LoggerMessage(Level = LogLevel.Information, Message = "Image upload body could not be read (too large: {TooLarge}, {Failure})")]
    private static partial void LogUnreadableForm(ILogger logger, bool tooLarge, string failure);
}
