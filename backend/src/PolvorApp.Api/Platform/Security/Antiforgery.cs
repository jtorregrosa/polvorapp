using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.HttpResults;
using PolvorApp.SharedKernel.Hosting;

namespace PolvorApp.Api.Platform.Security;

/// <summary>
/// Anti-forgery for the SPA (ADR-0004, design D6; spec: Sessions). <c>GET /api/auth/antiforgery</c>
/// stores the cookie token and hands the request token to the UI in a script-readable
/// <c>XSRF-TOKEN</c> cookie; every state-changing <c>/api</c> request must echo it in
/// <c>X-XSRF-TOKEN</c>, whether signed in or not.
/// </summary>
internal static partial class Antiforgery
{
    public const string HeaderName = "X-XSRF-TOKEN";
    public const string RequestTokenCookie = "XSRF-TOKEN";
    public const string ProblemCode = "antiforgery.invalid";

    public static IServiceCollection AddPlatformAntiforgery(this IServiceCollection services)
    {
        services.AddAntiforgery(options => options.HeaderName = HeaderName);
        services.AddOptions<AntiforgeryOptions>().Configure<IHostEnvironment>((options, environment) =>
        {
            options.Cookie.Name = "polvorapp.af";
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = LocalEnvironments.IsLocal(environment) ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        });
        return services;
    }

    /// <summary>Validates every non-safe <c>/api</c> request; place after authentication (tokens are bound to the user).</summary>
    public static IApplicationBuilder UsePlatformAntiforgery(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/api") && !IsSafe(context.Request.Method))
            {
                // Without the header the request is invalid anyway; checking first keeps the validator
                // from reading a form body (e.g. a large upload) to look for a form token.
                var antiforgery = context.RequestServices.GetRequiredService<IAntiforgery>();
                if (string.IsNullOrEmpty(context.Request.Headers[HeaderName]) || !await antiforgery.IsRequestValidAsync(context))
                {
                    LogRejected(context.RequestServices.GetRequiredService<ILogger<IAntiforgery>>());
                    await Results.Problem(
                        statusCode: StatusCodes.Status400BadRequest,
                        extensions: new Dictionary<string, object?> { ["code"] = ProblemCode }).ExecuteAsync(context);
                    return;
                }
            }

            await next(context);
        });

    public static IEndpointRouteBuilder MapAntiforgeryToken(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/auth/antiforgery", IssueToken)
            .WithName("GetAntiforgeryToken")
            .WithTags("Auth")
            .WithSummary("Issues the anti-forgery token the UI echoes in X-XSRF-TOKEN.")
            .AllowAnonymous();
        return endpoints;
    }

    private static NoContent IssueToken(HttpContext context, IAntiforgery antiforgery, IHostEnvironment environment)
    {
        var tokens = antiforgery.GetAndStoreTokens(context);
        context.Response.Cookies.Append(RequestTokenCookie, tokens.RequestToken ?? string.Empty, new CookieOptions
        {
            HttpOnly = false,
            SameSite = SameSiteMode.Strict,
            Secure = !LocalEnvironments.IsLocal(environment) || context.Request.IsHttps,
            Path = "/",
        });
        context.Response.Headers.CacheControl = "no-store";
        return TypedResults.NoContent();
    }

    private static bool IsSafe(string method) =>
        HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method) || HttpMethods.IsTrace(method);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Request rejected: missing or invalid anti-forgery token")]
    private static partial void LogRejected(ILogger logger);
}
