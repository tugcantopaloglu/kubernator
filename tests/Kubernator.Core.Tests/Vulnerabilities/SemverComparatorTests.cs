using Kubernator.Core.Vulnerabilities;

namespace Kubernator.Core.Tests.Vulnerabilities;

public sealed class SemverComparatorTests
{
    [Theory]
    [InlineData("1.0.0", "1.0.0", 0)]
    [InlineData("1.0.0", "1.0.1", -1)]
    [InlineData("2.0.0", "1.9.9", 1)]
    [InlineData("1.0.0", "1.0.0-rc1", 1)]
    [InlineData("1.0.0-rc1", "1.0.0-rc2", -1)]
    [InlineData("v1.2.3", "1.2.3", 0)]
    [InlineData("1.2.3+build.1", "1.2.3", 0)]
    [InlineData("10.0.0", "9.9.9", 1)]
    public void Compare_returns_expected_sign(string a, string b, int expected)
    {
        var sign = Math.Sign(SemverComparator.Compare(a, b));
        sign.Should().Be(expected);
    }

    [Fact]
    public void IsAffected_handles_introduced_and_fixed()
    {
        var range = new VersionRange { Type = "ECOSYSTEM", Introduced = "1.0.0", Fixed = "2.0.0" };
        SemverComparator.IsAffected("1.5.0", range).Should().BeTrue();
        SemverComparator.IsAffected("2.0.0", range).Should().BeFalse();
        SemverComparator.IsAffected("0.9.0", range).Should().BeFalse();
    }

    [Fact]
    public void IsAffected_unbounded_when_only_introduced()
    {
        var range = new VersionRange { Type = "ECOSYSTEM", Introduced = "1.0.0" };
        SemverComparator.IsAffected("99.0.0", range).Should().BeTrue();
        SemverComparator.IsAffected("0.5.0", range).Should().BeFalse();
    }

    [Fact]
    public void IsAffected_treats_explicit_versions_list_as_match()
    {
        var pkg = new AffectedPackage
        {
            Ecosystem = "NuGet",
            Name = "Demo",
            Versions = ["1.0.1", "1.0.2"]
        };
        SemverComparator.IsAffected("1.0.1", pkg).Should().BeTrue();
        SemverComparator.IsAffected("1.0.0", pkg).Should().BeFalse();
    }
}
