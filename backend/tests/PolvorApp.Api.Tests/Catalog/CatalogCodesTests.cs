using System.Text.Json;
using System.Text.Json.Serialization;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.SharedKernel.Codes;

namespace PolvorApp.Api.Tests.Catalog;

/// <summary>
/// Catalogue enums travel as culture-independent codes in the API and the database
/// (change add-federation-catalog, design D1/D3): one source of truth, unknown codes rejected.
/// </summary>
public sealed class CatalogCodesTests
{
    [Fact]
    public void Every_catalogue_enum_value_round_trips_through_its_code()
    {
        AssertRoundTrip([Side.Moorish, Side.Christian], ["MOORISH", "CHRISTIAN"]);
        AssertRoundTrip([WeaponKind.Trabuco, WeaponKind.Arcabuz, WeaponKind.Pistol], ["TRABUCO", "ARCABUZ", "PISTOL"]);
        AssertRoundTrip([Handedness.Right, Handedness.Left], ["RIGHT", "LEFT"]);
        AssertRoundTrip([WeaponSize.Normal, WeaponSize.Small], ["NORMAL", "SMALL"]);
    }

    [Theory]
    [InlineData("NEUTRAL")]
    [InlineData("moorish")]
    [InlineData(" MOORISH")]
    [InlineData("")]
    [InlineData(null)]
    public void Unknown_codes_are_rejected(string? code)
    {
        Assert.Null(EnumCodes.FromCode<Side>(code));
        Assert.Throws<FormatException>(() => EnumCodes.Parse<Side>(code!));
    }

    [Fact]
    public void Json_uses_the_same_codes()
    {
        Assert.Equal("\"CHRISTIAN\"", JsonSerializer.Serialize(Side.Christian));
        Assert.Equal(WeaponKind.Pistol, JsonSerializer.Deserialize<WeaponKind>("\"PISTOL\""));
    }

    [Theory]
    [InlineData("1")]
    [InlineData("\"1\"")]
    public void Json_rejects_integers(string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Side>(json));
    }

    [Fact]
    public void Json_never_writes_an_unnamed_value()
    {
        Assert.ThrowsAny<Exception>(() => JsonSerializer.Serialize((Side)7));
    }

    [Fact]
    public void Codes_are_listed_in_declaration_order_and_cannot_be_changed()
    {
        var all = EnumCodes.All<Reordered>();

        Assert.Equal(["SECOND", "FIRST"], all);
        Assert.False(all is string[]);
    }

    [Fact]
    public void An_enum_member_without_a_code_is_a_programming_error()
    {
        var error = Assert.Throws<TypeInitializationException>(() => EnumCodes.All<Uncoded>());
        Assert.IsType<InvalidOperationException>(error.InnerException);
    }

    private static void AssertRoundTrip<TEnum>(TEnum[] values, string[] codes)
        where TEnum : struct, Enum
    {
        Assert.Equal(codes, EnumCodes.All<TEnum>());
        foreach (var (value, code) in values.Zip(codes))
        {
            Assert.Equal(code, EnumCodes.ToCode(value));
            Assert.Equal(value, EnumCodes.Parse<TEnum>(code));
        }
    }

    private enum Reordered
    {
        [JsonStringEnumMemberName("SECOND")]
        Second = 2,

        [JsonStringEnumMemberName("FIRST")]
        First = 1,
    }

    private enum Uncoded
    {
        [JsonStringEnumMemberName("CODED")]
        Coded,

        Missing,
    }
}
