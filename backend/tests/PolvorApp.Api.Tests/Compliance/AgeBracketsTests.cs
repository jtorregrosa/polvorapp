using PolvorApp.ComplianceInsights.Statistics;
using PolvorApp.SharedKernel.Codes;

namespace PolvorApp.Api.Tests.Compliance;

/// <summary>Spec "Statistics (UC-07)": under 25, 25 to 34, 35 to 44, 45 or older (by their API codes).</summary>
public sealed class AgeBracketsTests
{
    [Theory]
    [InlineData(-1, "UNDER_25")]
    [InlineData(17, "UNDER_25")]
    [InlineData(24, "UNDER_25")]
    [InlineData(25, "FROM_25_TO_34")]
    [InlineData(34, "FROM_25_TO_34")]
    [InlineData(35, "FROM_35_TO_44")]
    [InlineData(44, "FROM_35_TO_44")]
    [InlineData(45, "FROM_45")]
    [InlineData(120, "FROM_45")]
    public void Every_age_falls_in_its_bracket(int age, string expected) =>
        Assert.Equal(EnumCodes.FromCode<AgeBracket>(expected), AgeBrackets.Of(age));
}
