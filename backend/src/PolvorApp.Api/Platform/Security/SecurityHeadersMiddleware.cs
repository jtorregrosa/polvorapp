namespace PolvorApp.Api.Platform.Security;

/// <summary>
/// Security headers on every API response (spec: Security response headers). The API returns
/// only data, so its policy denies every resource and all framing; nginx sets the UI policy.
/// No CORS policy is registered: the API is same-origin only (ADR-0011).
/// </summary>
internal sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers["Referrer-Policy"] = "no-referrer";
            headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
            headers["Permissions-Policy"] = "camera=(self), geolocation=(), microphone=()";
            return Task.CompletedTask;
        });

        return next(context);
    }
}
