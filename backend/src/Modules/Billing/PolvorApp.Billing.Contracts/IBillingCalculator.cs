namespace PolvorApp.Billing.Contracts;

/// <summary>
/// The pricing rule of the billing capability (UC-28; change add-billing-summary, design D1): the only
/// place that turns quantities and prices into amounts. Amounts are exact <see cref="decimal"/>s with
/// two decimals, never rounded through floating point.
/// </summary>
public interface IBillingCalculator
{
    /// <summary>
    /// The summary of one order, or of several orders of one edition whose quantities the caller has
    /// added up (spec: Edition billing (Admins)): adding quantities and pricing them once gives the same
    /// total as adding the orders' totals.
    /// </summary>
    /// <param name="quantities">What the order or orders hold; never negative.</param>
    /// <param name="prices">The edition's current prices.</param>
    /// <param name="state">Final only for a validated order, or for a set of orders all validated.</param>
    BillingSummary Summarise(BillingQuantities quantities, BillingPrices prices, BillingState state);
}
