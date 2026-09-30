using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace PolvorApp.IdentityAccess.Endpoints;

/// <summary>
/// Problem responses with a stable, culture-independent <c>code</c> the UI translates
/// (<c>identity:errors.&lt;code&gt;</c>, design D8). Titles are localised by the platform.
/// </summary>
internal static class Problems
{
    public const string InvalidCredentials = "auth.invalidCredentials";
    public const string InvalidCode = "auth.invalidCode";
    public const string LockedOut = "auth.lockedOut";
    public const string StepExpired = "auth.stepExpired";
    public const string Unauthenticated = "auth.unauthenticated";
    public const string InvalidLink = "auth.invalidLink";
    public const string InvalidPassword = "auth.invalidPassword";
    public const string WrongCurrentPassword = "account.wrongCurrentPassword";
    public const string EmailTaken = "users.emailTaken";
    public const string LastAdmin = "users.lastAdmin";
    public const string NotInvited = "users.notInvited";
    public const string NotEnrolled = "users.notEnrolled";
    public const string UserNotFound = "users.notFound";
    public const string EmailSendFailed = "email.sendFailed";
    public const string Validation = "validation";

    public static ProblemHttpResult Problem(int status, string code, IReadOnlyDictionary<string, object?>? extra = null)
    {
        var extensions = new Dictionary<string, object?> { ["code"] = code };
        foreach (var (key, value) in extra ?? new Dictionary<string, object?>())
        {
            extensions[key] = value;
        }

        return TypedResults.Problem(statusCode: status, extensions: extensions);
    }

    public static ProblemHttpResult Unauthorized(string code) => Problem(StatusCodes.Status401Unauthorized, code);

    public static ProblemHttpResult Conflict(string code) => Problem(StatusCodes.Status409Conflict, code);

    public static ProblemHttpResult Gone(string code) => Problem(StatusCodes.Status410Gone, code);

    /// <summary>A rejected password: the Identity error codes tell the UI which rules failed.</summary>
    public static ProblemHttpResult Password(IdentityResult result) =>
        Problem(StatusCodes.Status400BadRequest, InvalidPassword, new Dictionary<string, object?>
        {
            ["errors"] = result.Errors.Select(e => e.Code).ToArray(),
        });

    /// <summary>Invalid fields, named in <c>errors</c> with a reason code each.</summary>
    public static ProblemHttpResult Invalid(IReadOnlyDictionary<string, string> fields) =>
        Problem(StatusCodes.Status400BadRequest, Validation, new Dictionary<string, object?> { ["errors"] = fields });
}
