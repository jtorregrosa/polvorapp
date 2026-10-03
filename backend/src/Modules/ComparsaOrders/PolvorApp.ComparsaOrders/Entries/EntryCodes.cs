using System.Text.Json.Serialization;
using PolvorApp.SharedKernel.Codes;

namespace PolvorApp.ComparsaOrders.Entries;

/// <summary>Type of the percussion caps of an entry (glossary: <c>PercussionCaps</c>).</summary>
[JsonConverter(typeof(CodeEnumConverter<CapsType>))]
internal enum CapsType
{
    [JsonStringEnumMemberName("NORMAL")]
    Normal,

    [JsonStringEnumMemberName("SMALL")]
    Small,
}

/// <summary>Where an entry's weapon comes from (spec: Edition entries (BR-05, BR-07)).</summary>
[JsonConverter(typeof(CodeEnumConverter<WeaponSource>))]
internal enum WeaponSource
{
    /// <summary>One of the arquebusier's owned weapons.</summary>
    [JsonStringEnumMemberName("OWNED")]
    Owned,

    /// <summary>A model offered for rental in the edition (BR-07).</summary>
    [JsonStringEnumMemberName("RENTAL")]
    Rental,

    /// <summary>Lent by another arquebusier or an external owner (BR-09).</summary>
    [JsonStringEnumMemberName("LOAN")]
    Loan,

    /// <summary>No weapon, e.g. an arquebusier who only carries powder.</summary>
    [JsonStringEnumMemberName("NONE")]
    None,
}

/// <summary>The powder flask of an entry (glossary: <c>PowderFlask</c>).</summary>
[JsonConverter(typeof(CodeEnumConverter<FlaskOption>))]
internal enum FlaskOption
{
    [JsonStringEnumMemberName("OWNED")]
    Owned,

    [JsonStringEnumMemberName("RENTAL_1KG")]
    Rental1Kg,

    [JsonStringEnumMemberName("RENTAL_2KG")]
    Rental2Kg,

    [JsonStringEnumMemberName("NONE")]
    None,
}

/// <summary>Who lends the weapon of a loan (spec: Weapon loans (UC-13, BR-09)).</summary>
[JsonConverter(typeof(CodeEnumConverter<LenderKind>))]
internal enum LenderKind
{
    /// <summary>A registered arquebusier of any comparsa, with one of their owned weapons.</summary>
    [JsonStringEnumMemberName("ARQUEBUSIER")]
    Arquebusier,

    /// <summary>An owner who is not in PolvorApp, typed in by hand.</summary>
    [JsonStringEnumMemberName("EXTERNAL")]
    External,
}
