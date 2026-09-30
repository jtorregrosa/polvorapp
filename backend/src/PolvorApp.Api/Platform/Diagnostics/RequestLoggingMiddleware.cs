using System.Diagnostics;
using PolvorApp.SharedKernel.Diagnostics;

namespace PolvorApp.Api.Platform.Diagnostics;

/// <summary>
/// One log entry per request with method, route template, status, duration and trace id, and the
/// trace id returned in <c>X-Trace-Id</c>. Query strings, headers, bodies and unmatched raw paths
/// are never logged because they may carry personal data (NFR-12).
/// </summary>
internal sealed partial class RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
{
    public const string TraceIdHeader = "X-Trace-Id";
    private const string UnmatchedPath = "(unmatched)";

    public async Task InvokeAsync(HttpContext context)
    {
        var traceId = TraceIds.Current(context);
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[TraceIdHeader] = traceId;
            return Task.CompletedTask;
        });

        var started = Stopwatch.GetTimestamp();
        var failed = false;
        try
        {
            await next(context);
        }
        catch
        {
            // Development's exception page sits outside this middleware: record the crash as a 500.
            failed = true;
            throw;
        }
        finally
        {
            var elapsedMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            var path = RouteTemplate(context);
            var statusCode = failed ? StatusCodes.Status500InternalServerError : context.Response.StatusCode;
            LogRequest(logger, context.Request.Method, path, statusCode, elapsedMilliseconds, traceId);
        }
    }

    private static string RouteTemplate(HttpContext context) =>
        context.GetEndpoint() is RouteEndpoint endpoint && endpoint.RoutePattern.RawText is { } template
            ? template
            : UnmatchedPath;

    [LoggerMessage(Level = LogLevel.Information, Message = "HTTP {Method} {Path} responded {StatusCode} in {ElapsedMilliseconds:0.0} ms (trace {TraceId})")]
    private static partial void LogRequest(ILogger logger, string method, string path, int statusCode, double elapsedMilliseconds, string traceId);
}
