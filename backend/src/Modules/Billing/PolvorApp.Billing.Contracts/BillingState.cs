using System.Text.Json.Serialization;
using PolvorApp.SharedKernel.Codes;

namespace PolvorApp.Billing.Contracts;

/// <summary>Whether a billing summary can still change before the Federation validates the order (spec: Provisional or final).</summary>
[JsonConverter(typeof(CodeEnumConverter<BillingState>))]
public enum BillingState
{
    /// <summary>The order is not validated yet: <c>DRAFT</c>, <c>SUBMITTED</c> or <c>RETURNED</c>.</summary>
    [JsonStringEnumMemberName("PROVISIONAL")]
    Provisional,

    /// <summary>The order is validated. The amounts still follow Admin edits and price changes.</summary>
    [JsonStringEnumMemberName("FINAL")]
    Final,
}
