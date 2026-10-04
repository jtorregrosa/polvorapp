using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using PolvorApp.SharedKernel.Http;

namespace PolvorApp.IdentityAccess.Endpoints;

/// <summary>
/// Identity problem codes the UI translates (<c>identity:errors.&lt;code&gt;</c>, design D8), built
/// with the shared <see cref="ProblemResults"/>.
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
    public const string UserErased = "users.erased";
    public const string UserNotFound = "users.notFound";
    public const string EmailSendFailed = "email.sendFailed";
    public const string Validation = ProblemResults.Validation;

    public static ProblemHttpResult Problem(int status, string code, IReadOnlyDictionary<string, object?>? extra = null) =>
        ProblemResults.Problem(status, code, extra);

    public static ProblemHttpResult Unauthorized(string code) => Problem(StatusCodes.Status401Unauthorized, code);

    public static ProblemHttpResult Conflict(string code) => ProblemResults.Conflict(code);

    public static ProblemHttpResult Gone(string code) => Problem(StatusCodes.Status410Gone, code);

    /// <summary>A rejected password: the Identity error codes tell the UI which rules failed.</summary>
    public static ProblemHttpResult Password(IdentityResult result) =>
        Problem(StatusCodes.Status400BadRequest, InvalidPassword, new Dictionary<string, object?>
        {
            ["errors"] = result.Errors.Select(e => e.Code).ToArray(),
        });

    /// <summary>Invalid fields, named in <c>errors</c> with a reason code each.</summary>
    public static ProblemHttpResult Invalid(IReadOnlyDictionary<string, string> fields) => ProblemResults.Invalid(fields);
}
