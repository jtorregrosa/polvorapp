using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging;
using PolvorApp.SharedKernel.Images;
using PolvorApp.SharedKernel.Validation;

namespace PolvorApp.SharedKernel.Http;

/// <summary>
/// Reads an image upload: one <c>file</c> part of a <c>multipart/form-data</c> body
/// (add-arquebusier-photos design D5; add-comparsa-logos design D2). The form is read here rather
/// than bound, in memory through <see cref="MultipartForms"/>, and an oversized or malformed body
/// becomes the same field error as any other invalid file.
/// </summary>
public static class ImageUploads
{
    /// <summary>The form field that carries the image.</summary>
    public const string FileField = MultipartForms.FileField;

    /// <summary>Room for the multipart framing around the file.</summary>
    public const long FramingBytes = MultipartForms.FramingBytes;

    /// <summary>The request size limit for an upload of at most <paramref name="maxFileBytes"/>.</summary>
    public static long MaxRequestBytes(long maxFileBytes) => MultipartForms.MaxRequestBytes(maxFileBytes);

    /// <summary>
    /// The <c>file</c> part, or the field reason (<c>required</c>, <c>tooLarge</c>) why there is none.
    /// The endpoint must limit its request size to <see cref="MaxRequestBytes"/>, so the whole body
    /// fits the in-memory buffer.
    /// </summary>
    public static async Task<(IFormFile? File, string? Problem)> ReadFileAsync(
        HttpRequest request, long maxFileBytes, ILogger logger, CancellationToken cancellationToken)
    {
        var (form, problem) = await MultipartForms.ReadAsync(request, maxFileBytes, logger, cancellationToken);
        if (form is null)
        {
            return (null, problem);
        }

        return form.Files.GetFile(FileField) is { } file ? (file, null) : (null, InputFields.Required);
    }

    /// <summary>The field reason the UI translates for a rejected image.</summary>
    public static string Reason(ImageRejection rejection) => rejection switch
    {
        ImageRejection.TooLarge => MultipartForms.TooLarge,
        ImageRejection.UnsupportedFormat => "unsupportedFormat",
        ImageRejection.TooSmall => "tooSmall",
        ImageRejection.AspectRatio => "aspectRatio",
        _ => throw new ArgumentOutOfRangeException(nameof(rejection), rejection, "Unknown image rejection."),
    };

    /// <summary>A <c>400</c> naming the <c>file</c> field with <paramref name="reason"/>.</summary>
    public static ProblemHttpResult Invalid(string reason) =>
        ProblemResults.Invalid(new Dictionary<string, string> { [FileField] = reason });
}
