using System.Text.Json.Serialization;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.FestivalEditions.Contracts;
using PolvorApp.SharedKernel.Codes;

namespace PolvorApp.FestivalEditions.Endpoints;

/// <summary>Create an edition: its year and festival dates; the rest starts from the previous edition (design D5).</summary>
/// <param name="Year">Edition year, 2000 to 2100, unique; never changes afterwards.</param>
/// <param name="FestivalStartsOn">First festival day, <c>yyyy-MM-dd</c>, within the year.</param>
/// <param name="FestivalEndsOn">Last festival day, <c>yyyy-MM-dd</c>, within the year, not before the first.</param>
internal sealed record CreateEditionRequest(int? Year, string? FestivalStartsOn, string? FestivalEndsOn);

/// <summary>The four flat prices in euros, at most 9999.99 with two decimals; null is not set (only in a draft).</summary>
/// <param name="PowderPerKg">Price of one kilogram of powder.</param>
/// <param name="CapsBox">Price of one box of percussion caps.</param>
/// <param name="WeaponRental">Price of renting one weapon.</param>
/// <param name="FlaskRental">Price of renting one powder flask.</param>
internal sealed record EditionPricesRequest(decimal? PowderPerKg, decimal? CapsBox, decimal? WeaponRental, decimal? FlaskRental);

/// <summary>Edit an edition's dates, order window and prices; replaces all of them (spec: Edition management by Admins).</summary>
/// <param name="FestivalStartsOn">First festival day, <c>yyyy-MM-dd</c>.</param>
/// <param name="FestivalEndsOn">Last festival day, <c>yyyy-MM-dd</c>.</param>
/// <param name="OrdersOpenOn">Planned opening of the orders, <c>yyyy-MM-dd</c>; optional in a draft.</param>
/// <param name="OrdersCloseOn">Planned closing of the orders, <c>yyyy-MM-dd</c>; optional in a draft.</param>
/// <param name="Prices">The prices; each optional in a draft.</param>
/// <param name="Version">The version the edit is based on.</param>
internal sealed record UpdateEditionRequest(
    string? FestivalStartsOn,
    string? FestivalEndsOn,
    string? OrdersOpenOn,
    string? OrdersCloseOn,
    EditionPricesRequest? Prices,
    uint? Version);

/// <summary>Move an edition one step (spec: Edition lifecycle (UC-11)).</summary>
/// <param name="Status"><c>DRAFT</c>, <c>IN_PROGRESS</c> or <c>CLOSED</c>.</param>
/// <param name="Version">The version the move is based on.</param>
internal sealed record ChangeEditionStatusRequest(string? Status, uint? Version);

/// <summary>Open or close the orders of the edition in progress (spec: Opening and closing orders (UC-11, BR-10)).</summary>
/// <param name="Open">True opens them, false closes them.</param>
/// <param name="Version">The version the change is based on.</param>
internal sealed record SetEditionOrdersRequest(bool? Open, uint? Version);

/// <summary>The whole set of rental models offered in an edition (spec: Rental models offered in an edition (BR-07)).</summary>
/// <param name="WeaponModelIds">Catalogue weapon model identifiers, at most 100.</param>
internal sealed record EditionWeaponModelsRequest(IReadOnlyList<Guid>? WeaponModelIds);

/// <summary>Add or edit a calendar milestone (spec: Calendar milestones).</summary>
/// <param name="Date">Its date, <c>yyyy-MM-dd</c>.</param>
/// <param name="Title">1 to 100 characters after trimming, on one line.</param>
/// <param name="Notify">
/// Whether the milestone is reminded by email (add-notifications). Absent: off for a new milestone,
/// unchanged for an edit.
/// </param>
internal sealed record CalendarMilestoneRequest(string? Date, string? Title, bool? Notify = null);

/// <summary>An edition in the list.</summary>
/// <param name="Id">Edition identifier.</param>
/// <param name="Year">Edition year.</param>
/// <param name="FestivalStartsOn">First festival day.</param>
/// <param name="FestivalEndsOn">Last festival day.</param>
/// <param name="Status">Lifecycle status.</param>
/// <param name="OrdersOpen">Whether its orders are open; only for the edition in progress.</param>
/// <param name="IsCurrent">True for the edition in progress.</param>
internal sealed record EditionRowResponse(
    Guid Id, int Year, DateOnly FestivalStartsOn, DateOnly FestivalEndsOn, EditionStatus Status, bool OrdersOpen, bool IsCurrent);

/// <summary>The prices in euros, with two decimals; null when not set yet.</summary>
/// <param name="PowderPerKg">Price of one kilogram of powder.</param>
/// <param name="CapsBox">Price of one box of percussion caps.</param>
/// <param name="WeaponRental">Price of renting one weapon.</param>
/// <param name="FlaskRental">Price of renting one powder flask.</param>
internal sealed record EditionPricesResponse(decimal? PowderPerKg, decimal? CapsBox, decimal? WeaponRental, decimal? FlaskRental);

/// <summary>A model in the edition's set.</summary>
/// <param name="Id">Weapon model identifier.</param>
/// <param name="Label">Federation label.</param>
/// <param name="Kind">Weapon kind.</param>
/// <param name="Offered">False when the model has since been deactivated or made non-rentable: it is not offered.</param>
internal sealed record EditionWeaponModelResponse(Guid Id, string Label, WeaponKind Kind, bool Offered);

/// <summary>A calendar milestone.</summary>
/// <param name="Id">Milestone identifier.</param>
/// <param name="Date">Its date.</param>
/// <param name="Title">Its title.</param>
/// <param name="Notify">Whether it is reminded by email (add-notifications).</param>
internal sealed record CalendarMilestoneResponse(Guid Id, DateOnly Date, string Title, bool Notify);

/// <summary>Which order window date comes next.</summary>
[JsonConverter(typeof(CodeEnumConverter<EditionWindowKind>))]
internal enum EditionWindowKind
{
    /// <summary>The planned opening of the orders.</summary>
    [JsonStringEnumMemberName("OPENS")]
    Opens,

    /// <summary>The planned closing of the orders.</summary>
    [JsonStringEnumMemberName("CLOSES")]
    Closes,
}

/// <summary>The first order window date on or after today (Europe/Madrid).</summary>
/// <param name="Kind">Opening or closing.</param>
/// <param name="Date">The date.</param>
internal sealed record EditionNextWindow(EditionWindowKind Kind, DateOnly Date);

/// <summary>An edition with everything an edition page shows (design D4).</summary>
/// <param name="Id">Edition identifier.</param>
/// <param name="Year">Edition year.</param>
/// <param name="Status">Lifecycle status.</param>
/// <param name="OrdersOpen">Whether FiringChiefs may edit orders (BR-10).</param>
/// <param name="Version">Send it back with an edit, a move or an orders change.</param>
/// <param name="FestivalStartsOn">First festival day.</param>
/// <param name="FestivalEndsOn">Last festival day.</param>
/// <param name="OrdersOpenOn">Planned opening of the orders.</param>
/// <param name="OrdersCloseOn">Planned closing of the orders.</param>
/// <param name="Prices">The prices.</param>
/// <param name="WeaponModels">The models in the set, sorted by label.</param>
/// <param name="Milestones">The milestones, by date and then title.</param>
/// <param name="NextWindow">The next order window date, or null when both have passed or are not set.</param>
internal sealed record EditionResponse(
    Guid Id,
    int Year,
    EditionStatus Status,
    bool OrdersOpen,
    uint Version,
    DateOnly FestivalStartsOn,
    DateOnly FestivalEndsOn,
    DateOnly? OrdersOpenOn,
    DateOnly? OrdersCloseOn,
    EditionPricesResponse Prices,
    IReadOnlyList<EditionWeaponModelResponse> WeaponModels,
    IReadOnlyList<CalendarMilestoneResponse> Milestones,
    EditionNextWindow? NextWindow);

/// <summary>The current edition, or null when no edition is in progress.</summary>
/// <param name="Edition">The edition in progress.</param>
internal sealed record CurrentEditionResponse(EditionResponse? Edition);
