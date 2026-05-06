using Kubernator.Cli.Infrastructure;
using Kubernator.Core.Strategy;

namespace Kubernator.Cli.Tests;

public class ExposureBuilderTests
{
    [Fact]
    public void NullHostname_ReturnsNull()
    {
        var result = ExposureBuilder.Build(null, null, null, null, null, null, null, null, null, null, false, null);
        result.Should().BeNull();
    }

    [Fact]
    public void EmptyHostname_ReturnsNull()
    {
        var result = ExposureBuilder.Build("", null, null, null, null, null, null, null, null, null, false, null);
        result.Should().BeNull();
    }

    [Fact]
    public void DefaultTlsMode_IsSelfSigned()
    {
        var result = ExposureBuilder.Build("app.example.com", null, null, null, null, null, null, null, null, null, false, null);
        result.Should().NotBeNull();
        result!.TlsMode.Should().Be(TlsMode.SelfSigned);
    }

    [Theory]
    [InlineData("none", TlsMode.None)]
    [InlineData("off", TlsMode.None)]
    [InlineData("self-signed", TlsMode.SelfSigned)]
    [InlineData("self", TlsMode.SelfSigned)]
    [InlineData("cert-manager", TlsMode.CertManager)]
    [InlineData("certmanager", TlsMode.CertManager)]
    [InlineData("acme", TlsMode.CertManager)]
    [InlineData("user", TlsMode.UserProvided)]
    [InlineData("user-provided", TlsMode.UserProvided)]
    [InlineData("byo", TlsMode.UserProvided)]
    public void ParsesKnownTlsMode(string raw, TlsMode expected)
    {
        var result = ExposureBuilder.Build("app.example.com", null, raw, null, null, "issuer", null, "cert.pem", "key.pem", null, false, null);
        result!.TlsMode.Should().Be(expected);
    }

    [Fact]
    public void UnknownTlsMode_Throws()
    {
        var act = () => ExposureBuilder.Build("app.example.com", null, "bogus", null, null, null, null, null, null, null, false, null);
        act.Should().Throw<InvalidOperationException>().WithMessage("*unknown --tls value*");
    }

    [Fact]
    public void UserTlsWithoutCertOrKey_Throws()
    {
        var act = () => ExposureBuilder.Build("app.example.com", null, "user", null, null, null, null, null, null, null, false, null);
        act.Should().Throw<InvalidOperationException>().WithMessage("*--cert-file*--key-file*");
    }

    [Fact]
    public void UserTlsWithCertOnly_Throws()
    {
        var act = () => ExposureBuilder.Build("app.example.com", null, "user", null, null, null, null, "cert.pem", null, null, false, null);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void CertManagerWithoutIssuer_Throws()
    {
        var act = () => ExposureBuilder.Build("app.example.com", null, "cert-manager", null, null, null, null, null, null, null, false, null);
        act.Should().Throw<InvalidOperationException>().WithMessage("*--cert-issuer*");
    }

    [Fact]
    public void DefaultsAreApplied()
    {
        var result = ExposureBuilder.Build("app.example.com", null, null, null, null, null, null, null, null, null, false, null)!;
        result.IngressClassName.Should().Be("nginx");
        result.TlsSecretName.Should().Be("tls-cert");
        result.CertManagerIssuerKind.Should().Be("ClusterIssuer");
        result.Path.Should().Be("/");
        result.RedirectHttpToHttps.Should().BeTrue();
    }

    [Fact]
    public void NoHttpsRedirect_DisablesRedirect()
    {
        var result = ExposureBuilder.Build("app.example.com", null, null, null, null, null, null, null, null, null, true, null)!;
        result.RedirectHttpToHttps.Should().BeFalse();
    }

    private static readonly string[] MixedExtraHosts = ["alt.example.com", "", "  ", "alt2.example.com"];
    private static readonly string[] ExpectedExtraHosts = ["alt.example.com", "alt2.example.com"];

    [Fact]
    public void ExtraHosts_FilterEmpty()
    {
        var result = ExposureBuilder.Build("app.example.com", MixedExtraHosts,
            null, null, null, null, null, null, null, null, false, null)!;
        result.AdditionalHostnames.Should().BeEquivalentTo(ExpectedExtraHosts);
    }

    [Fact]
    public void OverridePortAndIngressPathPropagate()
    {
        var result = ExposureBuilder.Build("app.example.com", null, null, "traefik", "my-tls", null, null, null, null, "/api", false, 8080)!;
        result.OverridePort.Should().Be(8080);
        result.IngressClassName.Should().Be("traefik");
        result.TlsSecretName.Should().Be("my-tls");
        result.Path.Should().Be("/api");
    }
}
