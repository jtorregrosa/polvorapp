namespace PolvorApp.Api.Platform.Localization;

/// <summary>
/// Marker for the API's user-facing texts (<c>SharedResources*.resx</c> beside this file).
/// The neutral resources are Spanish (es-ES, the default culture). Valencian texts live in
/// <c>SharedResources.ca.resx</c>: ca-ES-valencia falls back to its parent culture <c>ca</c>, and
/// MSBuild does not produce satellite assemblies for the <c>ca-ES-valencia</c> variant name.
/// </summary>
internal sealed class SharedResources;
