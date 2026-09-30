using System.Text.Json.Serialization;
using PolvorApp.SharedKernel.Codes;

namespace PolvorApp.FederationCatalog.Contracts;

/// <summary>The side a comparsa belongs to (glossary: <c>Side</c>).</summary>
[JsonConverter(typeof(CodeEnumConverter<Side>))]
public enum Side
{
    [JsonStringEnumMemberName("MOORISH")]
    Moorish,

    [JsonStringEnumMemberName("CHRISTIAN")]
    Christian,
}
