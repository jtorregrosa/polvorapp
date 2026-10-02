using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using PolvorApp.SharedKernel.Validation;

namespace PolvorApp.SharedKernel.Http;

/// <summary>
/// Reads a spreadsheet upload (add-registry-import design D3): one <c>file</c> part and plain text
/// fields of a <c>multipart/form-data</c> body, in memory through <see cref="MultipartForms"/>. A
/// missing or empty file, an oversized or a malformed body become the field reasons <c>required</c>
/// and <c>tooLarge</c> of <c>file</c>. The file is never written anywhere.
/// </summary>
public static class SpreadsheetUploads
{
    /// <summary>The form field that carries the workbook.</summary>
    public const string FileField = MultipartForms.FileField;

    /// <summary>The request size limit for an upload of at most <paramref name="maxFileBytes"/>.</summary>
    public static long MaxRequestBytes(long maxFileBytes) => MultipartForms.MaxRequestBytes(maxFileBytes);

    /// <summary>
    /// The upload. The endpoint must limit its request size to <see cref="MaxRequestBytes"/>, so the
    /// whole body fits the in-memory buffer.
    /// </summary>
    public static async Task<SpreadsheetUpload> ReadAsync(
        HttpRequest request, long maxFileBytes, ILogger logger, CancellationToken cancellationToken)
    {
        var (form, problem) = await MultipartForms.ReadAsync(request, maxFileBytes, logger, cancellationToken);
        if (form is null)
        {
            return SpreadsheetUpload.Failed(problem!, null);
        }

        return form.Files.GetFile(FileField) is { Length: > 0 } file
            ? SpreadsheetUpload.Read(file, form)
            : SpreadsheetUpload.Failed(InputFields.Required, form);
    }
}
