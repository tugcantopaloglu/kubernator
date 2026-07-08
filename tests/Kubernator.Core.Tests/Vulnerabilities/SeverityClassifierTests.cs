using FluentAssertions;
using Kubernator.Core.Vulnerabilities;

namespace Kubernator.Core.Tests.Vulnerabilities;

public sealed class SeverityClassifierTests
{
    [Theory]
    [InlineData("CVSS_V3:CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:U/C:H/I:H/A:H", Severity.Critical)]
    [InlineData("CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:U/C:H/I:H/A:H", Severity.Critical)]
    [InlineData("CVSS:3.1/AV:N/AC:H/PR:L/UI:R/S:U/C:L/I:N/A:N", Severity.Low)]
    [InlineData("CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:U/C:N/I:N/A:H", Severity.High)]
    [InlineData("CVSS:3.0/AV:N/AC:L/PR:N/UI:N/S:C/C:H/I:H/A:H", Severity.Critical)]
    public void Computes_severity_from_cvss_vector(string raw, Severity expected)
    {
        SeverityClassifier.FromRaw(raw).Should().Be(expected);
    }

    [Theory]
    [InlineData("CRITICAL", Severity.Critical)]
    [InlineData("high", Severity.High)]
    [InlineData("Moderate", Severity.Medium)]
    [InlineData("LOW", Severity.Low)]
    public void Classifies_word_buckets(string raw, Severity expected)
    {
        SeverityClassifier.FromRaw(raw).Should().Be(expected);
    }

    [Theory]
    [InlineData("CVSS_V3:9.8", Severity.Critical)]
    [InlineData("CVSS_V3:3.0", Severity.Low)]
    [InlineData("7.5", Severity.High)]
    public void Classifies_numeric_scores(string raw, Severity expected)
    {
        SeverityClassifier.FromRaw(raw).Should().Be(expected);
    }

    [Fact]
    public void Computes_cvss_v4_vector()
    {
        SeverityClassifier.FromRaw("CVSS_V4:CVSS:4.0/AV:N/AC:L/AT:N/PR:N/UI:N/VC:H/VI:H/VA:H/SC:N/SI:N/SA:N")
            .Should().Be(Severity.Critical);
    }

    [Fact]
    public void Uncomputable_v2_vector_is_unknown_not_downgraded_to_version_number()
    {
        // A CVSS v2 vector we do not compute must not be misread as a low score.
        SeverityClassifier.FromRaw("CVSS_V2:AV:N/AC:L/Au:N/C:C/I:C/A:C").Should().Be(Severity.Unknown);
    }

    [Fact]
    public void Unknown_when_empty()
    {
        SeverityClassifier.FromRaw(null).Should().Be(Severity.Unknown);
        SeverityClassifier.FromRaw("").Should().Be(Severity.Unknown);
    }
}
