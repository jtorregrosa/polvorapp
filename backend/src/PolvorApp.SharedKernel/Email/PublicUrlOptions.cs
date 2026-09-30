namespace PolvorApp.SharedKernel.Email;

/// <summary>
/// The public address of the UI (<c>App__PublicBaseUrl</c>), used to build links in emails. The
/// host validates it at startup: an absolute http(s) URL without query, fragment or user info,
/// https outside local environments. It may carry a path prefix (e.g. <c>https://host/polvorapp</c>).
/// </summary>
public sealed class PublicUrlOptions
{
    public const string Key = "App:PublicBaseUrl";

    public Uri? PublicBaseUrl { get; set; }

    /// <summary>
    /// Builds an absolute UI link under the base URL (its path prefix kept) with an escaped query.
    /// Use <see cref="Uri.AbsoluteUri"/> when writing it: <see cref="Uri.ToString"/> unescapes.
    /// </summary>
    /// <param name="path">A UI route relative to the base, e.g. <c>invitations/accept</c>.</param>
    /// <param name="query">Query parameters; values are escaped.</param>
    public Uri Link(string path, IReadOnlyDictionary<string, string> query)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(query);
        var baseUrl = PublicBaseUrl ?? throw new InvalidOperationException($"The {Key} setting is required.");
        if (path.Length == 0 || path.StartsWith("//", StringComparison.Ordinal) || path.Contains('?', StringComparison.Ordinal)
            || path.Contains('#', StringComparison.Ordinal) || !Uri.IsWellFormedUriString(path, UriKind.Relative))
        {
            throw new ArgumentException("The link path must be a relative UI route without query or fragment.", nameof(path));
        }

        var builder = new UriBuilder(baseUrl)
        {
            Path = baseUrl.AbsolutePath.TrimEnd('/') + "/" + path.TrimStart('/'),
            Query = string.Join('&', query.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}")),
            Fragment = string.Empty,
        };
        return builder.Uri;
    }
}
