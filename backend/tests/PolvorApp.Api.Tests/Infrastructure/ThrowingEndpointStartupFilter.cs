using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace PolvorApp.Api.Tests.Infrastructure;

/// <summary>
/// Adds a test-only path that throws, at the end of the application's pipeline, so the exception
/// travels through the real error handling configured in <c>Program</c>.
/// </summary>
public sealed class ThrowingEndpointStartupFilter : IStartupFilter
{
    public const string Path = "/api/test/throw";
    public const string ExceptionMessage = "exception-message-sentinel";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        next(app);
        app.Use((HttpContext context, RequestDelegate nextMiddleware) =>
            context.Request.Path == Path
                ? throw new InvalidOperationException(ExceptionMessage)
                : nextMiddleware(context));
    };
}
