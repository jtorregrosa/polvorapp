namespace PolvorApp.SharedKernel.Localization;

/// <summary>The three UI languages (ADR-0007), as stored in a user's preferred <c>locale</c>.</summary>
public static class SupportedLocales
{
    public const string Spanish = "es-ES";
    public const string Valencian = "ca-ES-valencia";
    public const string English = "en";

    public static readonly IReadOnlyList<string> All = [Spanish, Valencian, English];

    public static bool IsSupported(string? locale) => locale is not null && All.Contains(locale, StringComparer.Ordinal);
}
