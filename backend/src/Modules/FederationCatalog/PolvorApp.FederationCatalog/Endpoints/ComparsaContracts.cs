using PolvorApp.FederationCatalog.Comparsas;
using PolvorApp.FederationCatalog.Contracts;

namespace PolvorApp.FederationCatalog.Endpoints;

/// <summary>A comparsa as the API returns it (spec: Comparsas).</summary>
/// <param name="Id">Comparsa identifier.</param>
/// <param name="Name">Unique name.</param>
/// <param name="Side">Side the comparsa belongs to.</param>
/// <param name="Active">False once deactivated.</param>
internal sealed record ComparsaResponse(Guid Id, string Name, Side Side, bool Active)
{
    public static ComparsaResponse From(Comparsa comparsa) => new(comparsa.Id, comparsa.Name, comparsa.Side, comparsa.Active);
}

/// <summary>Create or edit a comparsa. The side arrives as text so an invalid value is reported by field name.</summary>
/// <param name="Name">Name, 1 to 100 characters after trimming.</param>
/// <param name="Side"><c>MOORISH</c> or <c>CHRISTIAN</c>.</param>
internal sealed record ComparsaRequest(string? Name, string? Side);
