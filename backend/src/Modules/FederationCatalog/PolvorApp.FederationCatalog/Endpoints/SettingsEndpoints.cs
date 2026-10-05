using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PolvorApp.FederationCatalog.Logos;
using PolvorApp.FederationCatalog.Settings;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.SharedKernel.Email;
using PolvorApp.SharedKernel.Http;
using PolvorApp.SharedKernel.Security;

namespace PolvorApp.FederationCatalog.Endpoints;

/// <summary>
/// Spec "Federation settings" (add-federation-settings, design D2): Admins read every section and save
/// one at a time against the version they read; FiringChiefs get <c>403</c>. Every signed-in user
/// reads the identity through <c>GET /federation</c> (<see cref="FederationLogoEndpoints"/>).
/// </summary>
internal static class SettingsEndpoints
{
    /// <summary>A save based on an older version.</summary>
    public const string Modified = "federationSettings.modified";

    /// <summary>The settings row stayed locked by another change; retryable.</summary>
    public const string Busy = "federationSettings.busy";

    public static IEndpointRouteBuilder MapSettingsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/federation-settings").WithTags("Settings")
            .RequireAuthorization(AuthorizationPolicies.Admin)
            .ProducesProblem(StatusCodes.Status401Unauthorized).ProducesProblem(StatusCodes.Status403Forbidden);
        group.MapGet("/", GetAsync).WithName("GetFederationSettings").WithSummary("Every Federation settings section, with its version.");
        group.MapPut("/identity", SaveIdentityAsync).WithName("UpdateIdentitySettings")
            .WithSummary("Saves the Federation's names and public contact.").WithSaveProblems();
        group.MapPut("/emails", SaveEmailsAsync).WithName("UpdateEmailSettings")
            .WithSummary("Saves the email sender name and reply-to address.").WithSaveProblems();
        group.MapPut("/orders", SaveOrdersAsync).WithName("UpdateOrderSettings")
            .WithSummary("Saves the first close reminder lead time.").WithSaveProblems();
        group.MapPut("/calendar", SaveCalendarAsync).WithName("UpdateCalendarSettings")
            .WithSummary("Saves the milestone reminder lead time.").WithSaveProblems();
        return endpoints;
    }

    /// <summary>A save's body is a few short fields.</summary>
    private const long MaxRequestBytes = 8 * 1024;

    /// <summary>Saves are small JSON bodies under the Admin write rate limit (security review).</summary>
    private static RouteHandlerBuilder WithSaveProblems(this RouteHandlerBuilder builder) =>
        builder.WithMetadata(new RequestSizeLimitAttribute(MaxRequestBytes))
            .RequireRateLimiting(RateLimitPolicies.OrderWrites)
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge).ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

    private static async Task<Ok<FederationSettingsResponse>> GetAsync(
        FederationSettingsAdministration administration, IEmailSenderProfile sender, CancellationToken cancellationToken) =>
        TypedResults.Ok(FederationSettingsResponse.From(await administration.GetAsync(cancellationToken), sender.SenderAddress));

    private static Task<Results<Ok<FederationSettingsResponse>, ProblemHttpResult>> SaveIdentityAsync(
        IdentitySettingsRequest request, FederationSettingsAdministration administration, IEmailSenderProfile sender, CancellationToken cancellationToken) =>
        SaveAsync(request.Version, errors => SettingsInput.Identity(request, errors), administration.SaveIdentityAsync, sender, cancellationToken);

    private static Task<Results<Ok<FederationSettingsResponse>, ProblemHttpResult>> SaveEmailsAsync(
        EmailSettingsRequest request, FederationSettingsAdministration administration, IEmailSenderProfile sender, CancellationToken cancellationToken) =>
        SaveAsync(request.Version, errors => SettingsInput.Emails(request, errors), administration.SaveEmailsAsync, sender, cancellationToken);

    private static Task<Results<Ok<FederationSettingsResponse>, ProblemHttpResult>> SaveOrdersAsync(
        OrderSettingsRequest request, FederationSettingsAdministration administration, IEmailSenderProfile sender, CancellationToken cancellationToken) =>
        SaveAsync(request.Version, errors => SettingsInput.Orders(request, errors), administration.SaveOrdersAsync, sender, cancellationToken);

    private static Task<Results<Ok<FederationSettingsResponse>, ProblemHttpResult>> SaveCalendarAsync(
        CalendarSettingsRequest request, FederationSettingsAdministration administration, IEmailSenderProfile sender, CancellationToken cancellationToken) =>
        SaveAsync(request.Version, errors => SettingsInput.Calendar(request, errors), administration.SaveCalendarAsync, sender, cancellationToken);

    private static async Task<Results<Ok<FederationSettingsResponse>, ProblemHttpResult>> SaveAsync<TFields>(
        uint? requestVersion,
        Func<Dictionary<string, string>, TFields?> read,
        Func<uint, TFields, CancellationToken, Task<(SettingsOutcome Outcome, FederationSettings? Settings)>> save,
        IEmailSenderProfile sender,
        CancellationToken cancellationToken)
        where TFields : class
    {
        var errors = new Dictionary<string, string>();
        var version = SettingsInput.Version(requestVersion, errors);
        var fields = read(errors);
        if (version is not { } basedOn || fields is null || errors.Count > 0)
        {
            return ProblemResults.Invalid(errors);
        }

        return await save(basedOn, fields, cancellationToken) switch
        {
            (SettingsOutcome.Done, { } settings) => TypedResults.Ok(FederationSettingsResponse.From(settings, sender.SenderAddress)),
            (SettingsOutcome.Modified, _) => ProblemResults.Conflict(Modified),
            (SettingsOutcome.Busy, _) => ProblemResults.Problem(StatusCodes.Status503ServiceUnavailable, Busy),
            var (outcome, _) => throw new InvalidOperationException($"Unexpected settings outcome {outcome}."),
        };
    }

}
