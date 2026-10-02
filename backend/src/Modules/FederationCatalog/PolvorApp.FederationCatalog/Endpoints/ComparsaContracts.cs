using PolvorApp.FederationCatalog.Comparsas;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.FederationCatalog.Logos;

namespace PolvorApp.FederationCatalog.Endpoints;

/// <summary>A comparsa as the API returns it (spec: Comparsas; Logo access).</summary>
/// <param name="Id">Comparsa identifier.</param>
/// <param name="Name">Unique name.</param>
/// <param name="Side">Side the comparsa belongs to.</param>
/// <param name="Active">False once deactivated.</param>
/// <param name="Logo">The logo, without its image; null when the comparsa has none.</param>
internal sealed record ComparsaResponse(Guid Id, string Name, Side Side, bool Active, ComparsaLogoResponse? Logo)
{
    public static ComparsaResponse From(Comparsa comparsa) =>
        new(comparsa.Id, comparsa.Name, comparsa.Side, comparsa.Active, comparsa.Logo is { } logo ? ComparsaLogoResponse.From(logo) : null);
}

/// <summary>A comparsa's logo, without its image (spec: Logo access).</summary>
/// <param name="Version">Changes whenever the logo changes; the UI puts it in the image URL.</param>
/// <param name="Width">Stored width in pixels.</param>
/// <param name="Height">Stored height in pixels.</param>
/// <param name="UploadedAt">When the logo was uploaded.</param>
internal sealed record ComparsaLogoResponse(Guid Version, int Width, int Height, DateTimeOffset UploadedAt)
{
    public static ComparsaLogoResponse From(ComparsaLogo logo) => new(logo.Id, logo.Width, logo.Height, logo.UploadedAt);
}

/// <summary>Create or edit a comparsa. The side arrives as text so an invalid value is reported by field name.</summary>
/// <param name="Name">Name, 1 to 100 characters after trimming.</param>
/// <param name="Side"><c>MOORISH</c> or <c>CHRISTIAN</c>.</param>
internal sealed record ComparsaRequest(string? Name, string? Side);
