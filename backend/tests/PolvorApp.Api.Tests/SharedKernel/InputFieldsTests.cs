using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.SharedKernel.Validation;

namespace PolvorApp.Api.Tests.SharedKernel;

/// <summary>The field rules every module shares: trimmed NFC text without hidden characters, and coded enums.</summary>
public sealed class InputFieldsTests
{
    [Fact]
    public void Text_is_trimmed_and_normalised_to_NFC()
    {
        var errors = new Dictionary<string, string>();

        var text = InputFields.Text("  Jose\u0301 María  ", "name", 100, errors);

        Assert.Equal("José María", text);
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(null, InputFields.Required)]
    [InlineData("   ", InputFields.Required)]
    [InlineData("Ana\nLópez", InputFields.Invalid)]
    [InlineData("Ana\u200BLópez", InputFields.Invalid)]
    [InlineData("Ana\u202ELópez", InputFields.Invalid)]
    [InlineData("Ana\uE000", InputFields.Invalid)]
    [InlineData("Abcdef", InputFields.TooLong)]
    public void Invalid_text_is_reported_by_field(string? value, string reason)
    {
        var errors = new Dictionary<string, string>();

        Assert.Null(InputFields.Text(value, "name", 5, errors));
        Assert.Equal(reason, errors["name"]);
    }

    [Fact]
    public void A_lone_surrogate_is_invalid()
    {
        // Built in code: xUnit's test case serialisation would replace a lone surrogate in InlineData.
        var value = "Ana" + (char)0xD800;
        var errors = new Dictionary<string, string>();

        Assert.Null(InputFields.Text(value, "name", 50, errors));
        Assert.Equal(InputFields.Invalid, errors["name"]);
    }

    [Theory]
    [InlineData(null, InputFields.Required)]
    [InlineData("", InputFields.Required)]
    [InlineData("moorish", InputFields.Invalid)]
    [InlineData("NEUTRAL", InputFields.Invalid)]
    public void Invalid_required_codes_are_reported(string? code, string reason)
    {
        var errors = new Dictionary<string, string>();

        Assert.Null(InputFields.RequiredCode<Side>(code, "side", errors));
        Assert.Equal(reason, errors["side"]);
    }

    [Fact]
    public void Absent_optional_code_is_null_without_error()
    {
        var errors = new Dictionary<string, string>();

        Assert.Null(InputFields.OptionalCode<Side>(null, "side", errors));
        Assert.Equal(Side.Moorish, InputFields.OptionalCode<Side>("MOORISH", "side", errors));
        Assert.Empty(errors);
    }
}
