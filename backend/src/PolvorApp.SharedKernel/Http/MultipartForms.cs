using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using PolvorApp.SharedKernel.Validation;

namespace PolvorApp.SharedKernel.Http;

/// <summary>
/// Reads a <c>multipart/form-data</c> upload entirely in memory (an uploaded file never touches the
/// server's disk), mapping an oversized or malformed body to the field reasons <c>tooLarge</c> and
/// <c>required</c> (add-comparsa-logos design D2; add-registry-import design D3). The platform
/// middleware has already checked the anti-forgery header before the body is read.
/// </summary>
internal static partial class MultipartForms
{
    /// <summary>Room for the multipart framing around the file.</summary>
    public const long FramingBytes = 64 * 1024;

    /// <summary>The form field that carries the file.</summary>
    public const string FileField = "file";

    /// <summary>
    /// Parts a form may have: the file and a few short text fields. More parts make the form
    /// unreadable (<c>required</c>), which only a hand-made request can cause.
    /// </summary>
    public const int MaxParts = 4;

    /// <summary>Longest text field value; uploads send identifiers only.</summary>
    public const int MaxTextFieldLength = 1024;

    /// <summary>The field reason of a file over the limit.</summary>
    public const string TooLarge = "tooLarge";

    public static long MaxRequestBytes(long maxFileBytes) => maxFileBytes + FramingBytes;

    /// <summary>The form, or the field reason (<c>required</c>, <c>tooLarge</c>) why it cannot be read.</summary>
    public static async Task<(IFormCollection? Form, string? Problem)> ReadAsync(
        HttpRequest request, long maxFileBytes, ILogger logger, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFileBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaxRequestBytes(maxFileBytes), int.MaxValue, nameof(maxFileBytes));
        if (!(request.ContentType ?? string.Empty).StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase))
        {
            return (null, InputFields.Required);
        }

        // Fail closed when an endpoint forgot its RequestSizeLimit: the body can never exceed what
        // the in-memory buffer below is sized for.
        if (request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } bodySize
            && (bodySize.MaxRequestBodySize is null || bodySize.MaxRequestBodySize > MaxRequestBytes(maxFileBytes)))
        {
            bodySize.MaxRequestBodySize = MaxRequestBytes(maxFileBytes);
        }

        var options = new FormOptions
        {
            MultipartBodyLengthLimit = maxFileBytes,
            MemoryBufferThreshold = checked((int)MaxRequestBytes(maxFileBytes)),
            ValueCountLimit = MaxParts,
            ValueLengthLimit = MaxTextFieldLength,
            KeyLengthLimit = MaxTextFieldLength,
        };
        try
        {
            return (await request.ReadFormAsync(options, cancellationToken), null);
        }
        catch (BadHttpRequestException exception) when (exception.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return (null, TooLarge);
        }
        catch (Exception exception) when (exception is InvalidDataException or BadHttpRequestException)
        {
            // A part over the limit, or a malformed body: no readable file either way.
            var tooLarge = request.ContentLength > maxFileBytes || exception.Message.Contains("length limit", StringComparison.OrdinalIgnoreCase);
            var failure = exception.GetType().Name;
            LogUnreadableForm(logger, tooLarge, failure);
            return (null, tooLarge ? TooLarge : InputFields.Required);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Upload body could not be read (too large: {TooLarge}, {Failure})")]
    private static partial void LogUnreadableForm(ILogger logger, bool tooLarge, string failure);
}
