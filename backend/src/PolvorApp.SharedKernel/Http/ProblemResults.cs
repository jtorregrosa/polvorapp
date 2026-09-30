using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace PolvorApp.SharedKernel.Http;

/// <summary>
/// Problem responses with a stable, culture-independent <c>code</c> extension that the UI
/// translates. Titles are localised by the platform; each module owns its code constants.
/// </summary>
public static class ProblemResults
{
    /// <summary>The code of a <c>400</c> whose invalid fields are named in <c>errors</c>.</summary>
    public const string Validation = "validation";

    /// <summary>A problem with <paramref name="status"/>, the <c>code</c> extension and any <paramref name="extra"/> extensions; <c>code</c> is reserved.</summary>
    public static ProblemHttpResult Problem(int status, string code, IReadOnlyDictionary<string, object?>? extra = null)
    {
        var extensions = new Dictionary<string, object?>();
        foreach (var (key, value) in extra ?? new Dictionary<string, object?>())
        {
            extensions[key] = value;
        }

        extensions["code"] = code;
        return TypedResults.Problem(statusCode: status, extensions: extensions);
    }

    /// <summary>A <c>404</c>, also used for records outside the caller's comparsa scope (BR-12).</summary>
    public static ProblemHttpResult NotFound(string code) => Problem(StatusCodes.Status404NotFound, code);

    /// <summary>A <c>409</c> for a blocking data-integrity rule.</summary>
    public static ProblemHttpResult Conflict(string code) => Problem(StatusCodes.Status409Conflict, code);

    /// <summary>Invalid fields, named in <c>errors</c> with a reason code each.</summary>
    public static ProblemHttpResult Invalid(IReadOnlyDictionary<string, string> fields) =>
        Problem(StatusCodes.Status400BadRequest, Validation, new Dictionary<string, object?> { ["errors"] = fields });
}
