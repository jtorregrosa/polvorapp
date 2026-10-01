using System.Text.Json.Serialization;
using PolvorApp.SharedKernel.Codes;

namespace PolvorApp.ArquebusierRegistry.Contracts;

/// <summary>
/// The only status of an arquebusier (glossary: <c>ArquebusierStatus</c>, UC-05). Leaving the
/// Federation is a deletion, not a status.
/// </summary>
[JsonConverter(typeof(CodeEnumConverter<ArquebusierStatus>))]
public enum ArquebusierStatus
{
    /// <summary>Fires in the festival.</summary>
    [JsonStringEnumMemberName("ACTIVE")]
    Active,

    /// <summary>Does not fire (0 kg) but stays on the list, e.g. as a pickup proxy.</summary>
    [JsonStringEnumMemberName("RESERVE")]
    Reserve,
}
