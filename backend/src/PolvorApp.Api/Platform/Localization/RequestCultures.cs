using System.Globalization;
using Microsoft.AspNetCore.Localization;
using Microsoft.Net.Http.Headers;

namespace PolvorApp.Api.Platform.Localization;

/// <summary>Request culture from <c>Accept-Language</c> among the three UI locales (ADR-0007).</summary>
internal static class RequestCultures
{
    public const string Default = "es-ES";
    public const string Valencian = "ca-ES-valencia";
    public const string English = "en";

    public static readonly IReadOnlyList<string> Supported = [Default, Valencian, English];

    public static IServiceCollection AddPlatformLocalization(this IServiceCollection services)
    {
        services.AddLocalization();
        services.Configure<RequestLocalizationOptions>(options =>
        {
            var cultures = Supported.Select(name => new CultureInfo(name)).ToList();
            options.DefaultRequestCulture = new RequestCulture(Default);
            options.SupportedCultures = cultures;
            options.SupportedUICultures = cultures;
            options.RequestCultureProviders = [new PrimaryLanguageRequestCultureProvider()];
        });
        return services;
    }

    /// <summary>
    /// Maps a language tag to a supported culture by its primary subtag, like the UI does:
    /// <c>es*</c> → es-ES, <c>ca*</c> → ca-ES-valencia (the only Catalan variant offered), <c>en*</c> → en.
    /// </summary>
    public static string? Match(string languageTag)
    {
        ArgumentNullException.ThrowIfNull(languageTag);

        var primary = languageTag.Split('-', 2)[0];
        return primary.ToUpperInvariant() switch
        {
            "ES" => Default,
            "CA" => Valencian,
            "EN" => English,
            _ => null,
        };
    }

    private sealed class PrimaryLanguageRequestCultureProvider : RequestCultureProvider
    {
        public override Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext httpContext)
        {
            var match = httpContext.Request.GetTypedHeaders().AcceptLanguage
                .Where(language => language.Quality is null or > 0)
                .OrderByDescending(language => language.Quality ?? 1)
                .Select(language => Match(language.Value.Value ?? string.Empty))
                .FirstOrDefault(culture => culture is not null);

            return Task.FromResult(match is null ? null : new ProviderCultureResult(match));
        }
    }
}
