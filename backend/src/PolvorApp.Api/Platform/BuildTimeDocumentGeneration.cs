using System.Reflection;

namespace PolvorApp.Api.Platform;

/// <summary>
/// Build-time OpenAPI generation launches the entry point through the <c>GetDocument.Insider</c>
/// tool. Startup code that needs real configuration or external services checks this flag.
/// </summary>
internal static class BuildTimeDocumentGeneration
{
    public static bool IsActive { get; } =
        Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";
}
