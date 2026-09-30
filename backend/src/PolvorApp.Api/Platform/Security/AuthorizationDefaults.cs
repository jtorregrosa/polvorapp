using Microsoft.AspNetCore.Authorization;

namespace PolvorApp.Api.Platform.Security;

/// <summary>
/// Spec "Authenticated API by default": every endpoint of the <c>/api</c> group requires a
/// signed-in user unless it opts out with <c>AllowAnonymous()</c>. The requirement sits on the
/// group rather than in a fallback policy, so unknown routes still answer 404. The identity-access
/// module registers the authentication schemes; only a completed two-factor sign-in issues the
/// application cookie (design D6).
/// </summary>
internal static class AuthorizationDefaults
{
    public static IServiceCollection AddPlatformAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization();
        return services;
    }

    public static RouteGroupBuilder RequireSignedInUserByDefault(this RouteGroupBuilder api) =>
        api.RequireAuthorization(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
}
