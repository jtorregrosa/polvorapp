using System.Diagnostics;
using Microsoft.AspNetCore.Http.Features;

namespace PolvorApp.Api.Platform.Diagnostics;

/// <summary>The W3C trace id that correlates a response with its log entries (NFR-12).</summary>
internal static class TraceIds
{
    public static string Current(HttpContext context) =>
        (Activity.Current ?? context.Features.Get<IHttpActivityFeature>()?.Activity)?.TraceId.ToHexString()
        ?? context.TraceIdentifier;
}
