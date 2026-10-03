using System.Text.Json.Serialization;
using PolvorApp.SharedKernel.Codes;

namespace PolvorApp.Billing.Contracts;

/// <summary>
/// A line of a billing summary (spec: Billing summary of an order (UC-28)). Members are declared in
/// line order, the order in which the lines are returned and shown.
/// </summary>
[JsonConverter(typeof(CodeEnumConverter<BillingConcept>))]
public enum BillingConcept
{
    /// <summary>Powder in kilograms, at <c>powderPerKg</c>.</summary>
    [JsonStringEnumMemberName("POWDER")]
    Powder,

    /// <summary>Caps boxes of both types, at <c>capsBox</c>.</summary>
    [JsonStringEnumMemberName("CAPS")]
    Caps,

    /// <summary>Rented weapons of any model, at <c>weaponRental</c>.</summary>
    [JsonStringEnumMemberName("WEAPON_RENTAL")]
    WeaponRental,

    /// <summary>Rented flasks of either size, at <c>flaskRental</c>.</summary>
    [JsonStringEnumMemberName("FLASK_RENTAL")]
    FlaskRental,
}
