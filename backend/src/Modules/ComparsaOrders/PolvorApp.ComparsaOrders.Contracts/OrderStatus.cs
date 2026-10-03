using System.Text.Json.Serialization;
using PolvorApp.SharedKernel.Codes;

namespace PolvorApp.ComparsaOrders.Contracts;

/// <summary>
/// Status of a comparsa order (spec: Comparsa orders (UC-12)). A comparsa without an order in an
/// edition is "not prepared", which is not a stored status.
/// </summary>
[JsonConverter(typeof(CodeEnumConverter<OrderStatus>))]
public enum OrderStatus
{
    /// <summary>Prepared and being filled in; also after a FiringChief edits a submitted order.</summary>
    [JsonStringEnumMemberName("DRAFT")]
    Draft,

    /// <summary>Submitted to the Federation, by a FiringChief with the attestation or by an Admin.</summary>
    [JsonStringEnumMemberName("SUBMITTED")]
    Submitted,

    /// <summary>Returned by an Admin with a reason, to be fixed and submitted again.</summary>
    [JsonStringEnumMemberName("RETURNED")]
    Returned,

    /// <summary>Validated by an Admin; read-only for FiringChiefs.</summary>
    [JsonStringEnumMemberName("VALIDATED")]
    Validated,
}
