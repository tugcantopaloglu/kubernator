using Kubernator.Web.Auth;

namespace Kubernator.Web.Tests.Auth;

public sealed class TotpTests
{
    [Fact]
    public void Base32_round_trip()
    {
        var data = new byte[] { 0x00, 0x01, 0x02, 0x7F, 0xFF };
        var b32 = Totp.Base32Encode(data);
        Totp.Base32Decode(b32).Should().BeEquivalentTo(data);
    }

    [Fact]
    public void GenerateSecret_emits_base32_only()
    {
        var s = Totp.GenerateSecret();
        s.Should().NotBeNullOrWhiteSpace();
        s.Trim('=').Should().MatchRegex("^[A-Z2-7]+$");
        Totp.Base32Decode(s).Should().NotBeEmpty();
    }

    [Fact]
    public void Compute_then_verify_within_window()
    {
        var s = Totp.GenerateSecret();
        var code = Totp.ComputeCode(s);
        Totp.Verify(s, code).Should().BeTrue();
    }

    [Fact]
    public void Verify_rejects_wrong_code()
    {
        var s = Totp.GenerateSecret();
        var bad = "000000";
        var good = Totp.ComputeCode(s);
        var result = bad == good ? Totp.Verify(s, "111111") : Totp.Verify(s, bad);
        result.Should().BeFalse();
    }

    [Fact]
    public void VerifyWithCounter_returns_matching_counter()
    {
        var s = Totp.GenerateSecret();
        var now = DateTimeOffset.UtcNow;
        var code = Totp.ComputeCode(s, now);
        Totp.VerifyWithCounter(s, code, allowedSkew: 1, out var counter).Should().BeTrue();
        counter.Should().Be(now.ToUnixTimeSeconds() / Totp.DefaultPeriodSeconds);
    }

    [Fact]
    public void OtpAuthUri_matches_format()
    {
        var s = "JBSWY3DPEHPK3PXP";
        var uri = Totp.BuildOtpAuthUri("kubernator", "admin", s);
        uri.Should().StartWith("otpauth://totp/kubernator%3Aadmin?");
        uri.Should().Contain("secret=" + s);
        uri.Should().Contain("algorithm=SHA1");
    }

    [Fact]
    public void Empty_input_does_not_match()
    {
        var s = Totp.GenerateSecret();
        Totp.Verify(s, string.Empty).Should().BeFalse();
        Totp.Verify(s, "12345").Should().BeFalse();
        Totp.Verify(s, "123 456").Should().BeFalse();
        Totp.Verify(string.Empty, "123456").Should().BeFalse();
    }
}
