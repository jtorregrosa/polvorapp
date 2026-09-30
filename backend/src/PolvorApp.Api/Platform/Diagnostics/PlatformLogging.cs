using System.Diagnostics;

namespace PolvorApp.Api.Platform.Diagnostics;

/// <summary>Structured JSON logs correlated by W3C trace id, without personal data (NFR-12).</summary>
internal static class PlatformLogging
{
    private const string AspNetCoreActivitySource = "Microsoft.AspNetCore";

    // Process-wide listener: keeps ASP.NET Core creating request activities (and so trace ids)
    // although the hosting logger, which would otherwise trigger them, is switched off below.
    private static readonly ActivityListener RequestActivityListener = CreateRequestActivityListener();

    public static ILoggingBuilder AddPlatformLogging(this ILoggingBuilder logging)
    {
        _ = RequestActivityListener;

        logging.ClearProviders();
        logging.AddJsonConsole(options =>
        {
            options.IncludeScopes = true;
            options.UseUtcTimestamp = true;
            options.TimestampFormat = "O";
        });
        logging.Configure(options => options.ActivityTrackingOptions =
            ActivityTrackingOptions.TraceId | ActivityTrackingOptions.SpanId);

        // The hosting logger logs full URLs and opens a scope with the raw request path on every
        // entry; both may carry personal data. RequestLoggingMiddleware replaces its request logs.
        logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.None);
        logging.AddFilter("Microsoft.AspNetCore.Routing", LogLevel.Warning);
        return logging;
    }

    private static ActivityListener CreateRequestActivityListener()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == AspNetCoreActivitySource,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.PropagationData,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }
}
