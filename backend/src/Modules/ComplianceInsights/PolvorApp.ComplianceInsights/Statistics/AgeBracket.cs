using System.Text.Json.Serialization;
using PolvorApp.SharedKernel.Codes;

namespace PolvorApp.ComplianceInsights.Statistics;

/// <summary>Age brackets of the statistics (spec: Statistics), as in the Federation's former sheet.</summary>
[JsonConverter(typeof(CodeEnumConverter<AgeBracket>))]
internal enum AgeBracket
{
    /// <summary>Younger than 25.</summary>
    [JsonStringEnumMemberName("UNDER_25")]
    Under25,

    /// <summary>25 to 34.</summary>
    [JsonStringEnumMemberName("FROM_25_TO_34")]
    From25To34,

    /// <summary>35 to 44.</summary>
    [JsonStringEnumMemberName("FROM_35_TO_44")]
    From35To44,

    /// <summary>45 or older.</summary>
    [JsonStringEnumMemberName("FROM_45")]
    From45,
}

internal static class AgeBrackets
{
    public static AgeBracket Of(int age) => age switch
    {
        < 25 => AgeBracket.Under25,
        < 35 => AgeBracket.From25To34,
        < 45 => AgeBracket.From35To44,
        _ => AgeBracket.From45,
    };
}
