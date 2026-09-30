using System.Text;
using PolvorApp.IdentityAccess.Security;

namespace PolvorApp.Api.Tests.Identity;

/// <summary>Spec "Mandatory two-factor enrolment": RFC 6238, 6 digits, 30-second step.</summary>
public sealed class TotpTests
{
    // RFC 6238 appendix B, SHA-1 seed "12345678901234567890" (6-digit truncation of the 8-digit vectors).
    private static readonly byte[] RfcKey = Encoding.ASCII.GetBytes("12345678901234567890");

    [Theory]
    [InlineData(59, "287082")]
    [InlineData(1111111109, "081804")]
    [InlineData(1234567890, "005924")]
    [InlineData(2000000000, "279037")]
    public void Codes_match_the_rfc_6238_test_vectors(long unixSeconds, string expected) =>
        Assert.Equal(expected, Totp.Compute(RfcKey, Totp.StepAt(DateTimeOffset.FromUnixTimeSeconds(unixSeconds))));

    [Fact]
    public void Base32_keys_decode_as_authenticator_apps_read_them() =>
        Assert.Equal(RfcKey, Totp.DecodeBase32("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ"));

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public void A_code_from_the_previous_current_or_next_step_is_accepted(int offset)
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1234567890);
        var code = Totp.Compute(RfcKey, Totp.StepAt(now) + offset);

        Assert.Equal(Totp.StepAt(now) + offset, Totp.MatchStep(RfcKey, code, now));
    }

    [Fact]
    public void A_code_two_steps_away_is_refused()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1234567890);

        Assert.Null(Totp.MatchStep(RfcKey, Totp.Compute(RfcKey, Totp.StepAt(now) - 2), now));
    }

    [Theory]
    [InlineData("005 924", "005924")]
    [InlineData("005-924", "005924")]
    [InlineData("00592", null)]
    [InlineData("0059245", null)]
    [InlineData("abcdef", null)]
    [InlineData(null, null)]
    public void Typed_codes_are_normalised(string? typed, string? expected) =>
        Assert.Equal(expected, Totp.Normalize(typed));
}
