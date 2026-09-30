using System.Reflection;

namespace PolvorApp.Api.Platform.SystemInfo;

/// <summary>Application version, shown in the UI footer. No configuration or host data (spec: System information endpoint).</summary>
/// <param name="Version">Semantic version of the running build.</param>
/// <param name="Commit">Source commit the build was produced from, or <c>unknown</c>.</param>
public sealed record SystemInfoResponse(string Version, string Commit)
{
    private const string UnknownCommit = "unknown";

    /// <summary>Splits an informational version such as <c>1.4.0+3f2a9c1</c> into version and commit.</summary>
    public static SystemInfoResponse FromInformationalVersion(string informationalVersion)
    {
        ArgumentNullException.ThrowIfNull(informationalVersion);

        var separator = informationalVersion.IndexOf('+', StringComparison.Ordinal);
        if (separator < 0)
        {
            return new(informationalVersion, UnknownCommit);
        }

        var commit = informationalVersion[(separator + 1)..];
        return new(informationalVersion[..separator], commit.Length == 0 ? UnknownCommit : commit);
    }
}

internal static class SystemInfoEndpoints
{
    private static readonly SystemInfoResponse Current = SystemInfoResponse.FromInformationalVersion(
        typeof(SystemInfoEndpoints).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "0.0.0");

    public static IEndpointRouteBuilder MapSystemInfo(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/system/info", () => TypedResults.Ok(Current))
            .WithName("GetSystemInfo")
            .WithTags("System")
            .WithSummary("Application version and build commit.")
            .AllowAnonymous();
        return endpoints;
    }
}
