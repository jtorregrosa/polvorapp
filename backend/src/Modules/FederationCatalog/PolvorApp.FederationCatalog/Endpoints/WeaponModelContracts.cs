using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.FederationCatalog.WeaponModels;

namespace PolvorApp.FederationCatalog.Endpoints;

/// <summary>A weapon model as the API returns it (spec: Weapon models).</summary>
/// <param name="Id">Model identifier.</param>
/// <param name="Kind">Weapon kind.</param>
/// <param name="Side">Side; may be null for a pistol.</param>
/// <param name="Handedness">Handedness; may be null for a pistol.</param>
/// <param name="Size">Size; may be null for a pistol.</param>
/// <param name="Rentable">Whether editions may offer it for rental; never for a pistol.</param>
/// <param name="Label">Federation label, shown as entered.</param>
/// <param name="Active">False once deactivated.</param>
internal sealed record WeaponModelResponse(
    Guid Id, WeaponKind Kind, Side? Side, Handedness? Handedness, WeaponSize? Size, bool Rentable, string Label, bool Active)
{
    public static WeaponModelResponse From(WeaponModel model) =>
        new(model.Id, model.Kind, model.Side, model.Handedness, model.Size, model.Rentable, model.Label, model.Active);
}

/// <summary>Create or edit a weapon model. Codes arrive as text so an invalid value is reported by field name.</summary>
/// <param name="Kind"><c>TRABUCO</c>, <c>ARCABUZ</c> or <c>PISTOL</c>.</param>
/// <param name="Side"><c>MOORISH</c> or <c>CHRISTIAN</c>; required unless the kind is <c>PISTOL</c>.</param>
/// <param name="Handedness"><c>RIGHT</c> or <c>LEFT</c>; required unless the kind is <c>PISTOL</c>.</param>
/// <param name="Size"><c>NORMAL</c> or <c>SMALL</c>; required unless the kind is <c>PISTOL</c>.</param>
/// <param name="Rentable">Whether it can be rented; required, and false for a pistol (BR-07).</param>
/// <param name="Label">Federation label, 1 to 100 characters after trimming.</param>
internal sealed record WeaponModelRequest(string? Kind, string? Side, string? Handedness, string? Size, bool? Rentable, string? Label);
