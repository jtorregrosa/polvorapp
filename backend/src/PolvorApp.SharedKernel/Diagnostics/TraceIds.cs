using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace PolvorApp.SharedKernel.Diagnostics;

/// <summary>
/// The W3C trace id that correlates a response with its log entries and audit entries (NFR-12).
/// One source for the <c>X-Trace-Id</c> header, problem details and the audit trail.
/// </summary>
public static class TraceIds
{
    public static string Current(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return FromActivity(context) ?? context.TraceIdentifier;
    }

    /// <summary>The current trace id, or null outside a request and without an activity.</summary>
    public static string? Find(HttpContext? context) =>
        context is null ? Activity.Current?.TraceId.ToHexString() : Current(context);

    private static string? FromActivity(HttpContext context) =>
        (Activity.Current ?? context.Features.Get<IHttpActivityFeature>()?.Activity)?.TraceId.ToHexString();
}
