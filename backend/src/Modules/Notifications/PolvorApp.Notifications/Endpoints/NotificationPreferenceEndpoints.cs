using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.Notifications.Contracts;
using PolvorApp.Notifications.Preferences;
using PolvorApp.SharedKernel.Codes;
using PolvorApp.SharedKernel.Http;
using PolvorApp.SharedKernel.Security;

namespace PolvorApp.Notifications.Endpoints;

/// <summary>One kind and whether it is on.</summary>
/// <param name="Kind">The notification kind.</param>
/// <param name="Enabled">Whether the user receives it.</param>
internal sealed record NotificationPreferenceResponse(NotificationKind Kind, bool Enabled);

/// <summary>The kinds of the user's role, in a fixed order (spec: Notification preferences).</summary>
/// <param name="Kinds">Each kind with whether it is on.</param>
internal sealed record NotificationPreferencesResponse(IReadOnlyList<NotificationPreferenceResponse> Kinds);

/// <summary>A kind to turn on or off.</summary>
/// <param name="Kind">A <c>NotificationKind</c> code of the user's role.</param>
/// <param name="Enabled">Whether the user wants it.</param>
internal sealed record NotificationPreferenceRequest(string? Kind, bool? Enabled);

/// <summary>The kinds to change; kinds left out keep their value.</summary>
/// <param name="Kinds">Each kind at most once.</param>
internal sealed record NotificationPreferencesRequest(IReadOnlyList<NotificationPreferenceRequest>? Kinds);

/// <summary>Spec "Notification preferences": every signed-in user reads and changes their own, and only their own.</summary>
internal static class NotificationPreferenceEndpoints
{
    /// <summary>Well above four kinds, well below anything that could hurt.</summary>
    private const long MaxRequestBytes = 4 * 1024;

    public static IEndpointRouteBuilder MapNotificationPreferenceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/account/notification-preferences").WithTags("Notifications")
            .ProducesProblem(StatusCodes.Status401Unauthorized);
        group.MapGet("/", GetAsync).WithName("GetNotificationPreferences")
            .WithSummary("The signed-in user's notification kinds and whether each is on.");
        group.MapPut("/", SaveAsync).WithName("SaveNotificationPreferences")
            .WithSummary("Turns the signed-in user's notification kinds on or off; kinds left out keep their value.")
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBytes))
            .RequireRateLimiting(RateLimitPolicies.PersonalDataWrites)
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status429TooManyRequests);
        return endpoints;
    }

    private static async Task<Ok<NotificationPreferencesResponse>> GetAsync(
        ICurrentUser currentUser, NotificationPreferences preferences, CancellationToken cancellationToken)
    {
        var (userId, role) = SignedIn(currentUser);
        return TypedResults.Ok(Response(await preferences.GetAsync(userId, role, cancellationToken)));
    }

    private static async Task<Results<Ok<NotificationPreferencesResponse>, ProblemHttpResult>> SaveAsync(
        NotificationPreferencesRequest request, ICurrentUser currentUser, NotificationPreferences preferences, CancellationToken cancellationToken)
    {
        var (userId, role) = SignedIn(currentUser);
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        var changes = Read(request, role, errors);
        if (errors.Count > 0)
        {
            return ProblemResults.Invalid(errors);
        }

        return TypedResults.Ok(Response(await preferences.SaveAsync(userId, role, changes, cancellationToken)));
    }

    /// <summary>Validates every item, naming each invalid field as <c>kinds[i].kind</c> or <c>kinds[i].enabled</c>.</summary>
    internal static List<NotificationPreference> Read(NotificationPreferencesRequest request, UserRole role, Dictionary<string, string> errors)
    {
        var changes = new List<NotificationPreference>();
        if (request.Kinds is null)
        {
            errors["kinds"] = "required";
            return changes;
        }

        for (var i = 0; i < request.Kinds.Count; i++)
        {
            var item = request.Kinds[i];
            var field = $"kinds[{i}]";
            if (item is null)
            {
                errors[field] = "required";
                continue;
            }

            var kind = EnumCodes.FromCode<NotificationKind>(item.Kind);
            if (kind is not { } known)
            {
                errors[$"{field}.kind"] = item.Kind is null ? "required" : "invalid";
            }
            else if (!NotificationKinds.AppliesTo(known, role))
            {
                errors[$"{field}.kind"] = "notApplicable";
            }
            else if (changes.Exists(c => c.Kind == known))
            {
                errors[$"{field}.kind"] = "duplicate";
            }

            if (item.Enabled is null)
            {
                errors[$"{field}.enabled"] = "required";
            }

            if (kind is { } valid && item.Enabled is { } enabled)
            {
                changes.Add(new NotificationPreference(valid, enabled));
            }
        }

        return changes;
    }

    /// <summary>The fallback authorisation policy requires a session, so both are present.</summary>
    private static (Guid UserId, UserRole Role) SignedIn(ICurrentUser currentUser) =>
        currentUser is { UserId: { } userId, Role: { } role }
            ? (userId, role)
            : throw new InvalidOperationException("The notification preferences need a signed-in user.");

    private static NotificationPreferencesResponse Response(IReadOnlyList<NotificationPreference> preferences) =>
        new(preferences.Select(p => new NotificationPreferenceResponse(p.Kind, p.Enabled)).ToList());
}
