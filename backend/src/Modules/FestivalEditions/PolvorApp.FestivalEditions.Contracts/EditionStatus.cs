using System.Text.Json.Serialization;
using PolvorApp.SharedKernel.Codes;

namespace PolvorApp.FestivalEditions.Contracts;

/// <summary>
/// Lifecycle of a festival edition (glossary: <c>EditionStatus</c>; spec: Edition lifecycle). Moves
/// go one step at a time, forward or back. Whether FiringChiefs may edit orders is the separate
/// <c>ordersOpen</c> flag of the edition in progress (BR-10).
/// </summary>
[JsonConverter(typeof(CodeEnumConverter<EditionStatus>))]
public enum EditionStatus
{
    /// <summary>In preparation: only Admins see it.</summary>
    [JsonStringEnumMemberName("DRAFT")]
    Draft,

    /// <summary>The current edition; at most one at a time.</summary>
    [JsonStringEnumMemberName("IN_PROGRESS")]
    InProgress,

    /// <summary>The festival is over; kept as history.</summary>
    [JsonStringEnumMemberName("CLOSED")]
    Closed,
}
