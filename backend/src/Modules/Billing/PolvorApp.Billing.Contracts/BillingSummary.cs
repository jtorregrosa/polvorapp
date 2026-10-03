namespace PolvorApp.Billing.Contracts;

/// <summary>What an order, or several, holds of each charged item (spec: Billing summary of an order (UC-28)).</summary>
/// <param name="PowderKg">Powder in kilograms.</param>
/// <param name="CapsBoxes">Caps boxes, <c>NORMAL</c> and <c>SMALL</c> together.</param>
/// <param name="WeaponRentals">Entries with weapon source <c>RENTAL</c>, whatever the model.</param>
/// <param name="FlaskRentals">Entries with a rented flask, 1 kg or 2 kg.</param>
public sealed record BillingQuantities(int PowderKg, int CapsBoxes, int WeaponRentals, int FlaskRentals);

/// <summary>The four flat prices of an edition, in euros with at most two decimals; null when not set (only in a draft).</summary>
public sealed record BillingPrices(decimal? PowderPerKg, decimal? CapsBox, decimal? WeaponRental, decimal? FlaskRental);

/// <summary>One line of a billing summary.</summary>
/// <param name="Concept">What is charged.</param>
/// <param name="Quantity">How many units.</param>
/// <param name="UnitPrice">The edition's price with two decimals, or null when it is not set.</param>
/// <param name="Amount"><paramref name="Quantity"/> × <paramref name="UnitPrice"/>, or null when the price is not set.</param>
public sealed record BillingLine(BillingConcept Concept, int Quantity, decimal? UnitPrice, decimal? Amount);

/// <summary>
/// The amount a comparsa owes the Federation for an edition (glossary: <c>BillingSummary</c>), derived
/// and never stored (spec: Billing is always derived).
/// </summary>
/// <param name="Lines">The four lines, in <see cref="BillingConcept"/> order.</param>
/// <param name="Total">The sum of the amounts, or null when a price is missing.</param>
/// <param name="State">Provisional until the order is validated.</param>
/// <param name="MissingPrices">The concepts whose price is not set, in line order; the total is null exactly when there is one.</param>
public sealed record BillingSummary(IReadOnlyList<BillingLine> Lines, decimal? Total, BillingState State, IReadOnlyList<BillingConcept> MissingPrices);
