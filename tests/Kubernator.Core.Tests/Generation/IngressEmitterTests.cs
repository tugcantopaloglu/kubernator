using System.Security.Cryptography.X509Certificates;
using Kubernator.Core.Generation;
using Kubernator.Core.Strategy;
using Kubernator.Core.Tests.Fixtures;
using Kubernator.Core.Tests.Strategy;
using Kubernator.Core.Tls;

namespace Kubernator.Core.Tests.Generation;

public sealed class IngressEmitterTests
{
    private readonly StrategySelector strategy = new();
    private readonly GenerationService generation = new();

    [Fact]
    public async Task SelfSigned_emits_ingress_and_tls_secret()
    {
        using var temp = TempPublishOutput.Create();
        var plan = strategy.Plan(SampleApp.AspNet(), new StrategyOptions
        {
            Exposure = new ExposureOptions
            {
                PrimaryHostname = "demo.example.com",
                AdditionalHostnames = ["www.demo.example.com"],
                TlsMode = TlsMode.SelfSigned
            }
        });

        await generation.GenerateAsync(plan, new GenerationOptions { OutputDirectory = temp.Path, Namespace = "demo" });

        var ingress = await File.ReadAllTextAsync(Path.Combine(temp.Path, "kubernetes", "ingress.yaml"));
        ingress.Should().Contain("kind: Ingress");
        ingress.Should().Contain("ingressClassName: nginx");
        ingress.Should().Contain("demo.example.com");
        ingress.Should().Contain("www.demo.example.com");
        ingress.Should().Contain("secretName: tls-cert");

        var secret = await File.ReadAllTextAsync(Path.Combine(temp.Path, "kubernetes", "tls-secret.yaml"));
        secret.Should().Contain("type: kubernetes.io/tls");
        secret.Should().Contain("tls.crt:");
        secret.Should().Contain("tls.key:");
    }

    [Fact]
    public async Task CertManager_emits_certificate_resource_no_secret()
    {
        using var temp = TempPublishOutput.Create();
        var plan = strategy.Plan(SampleApp.AspNet(), new StrategyOptions
        {
            Exposure = new ExposureOptions
            {
                PrimaryHostname = "api.example.com",
                TlsMode = TlsMode.CertManager,
                CertManagerIssuerName = "letsencrypt-prod",
                CertManagerIssuerKind = "ClusterIssuer"
            }
        });

        await generation.GenerateAsync(plan, new GenerationOptions { OutputDirectory = temp.Path });

        File.Exists(Path.Combine(temp.Path, "kubernetes", "tls-secret.yaml")).Should().BeFalse();
        var cert = await File.ReadAllTextAsync(Path.Combine(temp.Path, "kubernetes", "certificate.yaml"));
        cert.Should().Contain("apiVersion: cert-manager.io/v1");
        cert.Should().Contain("kind: Certificate");
        cert.Should().Contain("name: letsencrypt-prod");
        cert.Should().Contain("kind: ClusterIssuer");
    }

    [Fact]
    public async Task UserProvided_loads_pem_files_from_disk()
    {
        using var temp = TempPublishOutput.Create();
        var material = SelfSignedCertificateGenerator.Generate("byo.example.com", []);
        var certPath = Path.Combine(temp.Path, "tls.crt");
        var keyPath = Path.Combine(temp.Path, "tls.key");
        await File.WriteAllTextAsync(certPath, material.CertificatePem);
        await File.WriteAllTextAsync(keyPath, material.PrivateKeyPem);

        var plan = strategy.Plan(SampleApp.AspNet(), new StrategyOptions
        {
            Exposure = new ExposureOptions
            {
                PrimaryHostname = "byo.example.com",
                TlsMode = TlsMode.UserProvided,
                UserCertificatePemPath = certPath,
                UserPrivateKeyPemPath = keyPath
            }
        });

        await generation.GenerateAsync(plan, new GenerationOptions { OutputDirectory = temp.Path });

        var secret = await File.ReadAllTextAsync(Path.Combine(temp.Path, "kubernetes", "tls-secret.yaml"));
        secret.Should().Contain("type: kubernetes.io/tls");
    }

    [Fact]
    public void SelfSigned_certificate_has_correct_san_and_eku()
    {
        var material = SelfSignedCertificateGenerator.Generate("test.example.com", ["alt.example.com"]);
        material.CertificatePem.Should().Contain("BEGIN CERTIFICATE");
        material.PrivateKeyPem.Should().Contain("BEGIN PRIVATE KEY");

        var pemBody = material.CertificatePem
            .Replace("-----BEGIN CERTIFICATE-----", string.Empty, StringComparison.Ordinal)
            .Replace("-----END CERTIFICATE-----", string.Empty, StringComparison.Ordinal)
            .Replace("\n", string.Empty, StringComparison.Ordinal)
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Trim();
        var der = Convert.FromBase64String(pemBody);
        using var cert = X509CertificateLoader.LoadCertificate(der);

        cert.Subject.Should().Contain("test.example.com");
        cert.NotAfter.Should().BeAfter(DateTime.UtcNow.AddDays(30));
        cert.Extensions.OfType<X509SubjectAlternativeNameExtension>().Should().NotBeEmpty();
    }

    [Fact]
    public async Task None_mode_emits_ingress_without_tls_block()
    {
        using var temp = TempPublishOutput.Create();
        var plan = strategy.Plan(SampleApp.AspNet(), new StrategyOptions
        {
            Exposure = new ExposureOptions
            {
                PrimaryHostname = "internal.local",
                TlsMode = TlsMode.None
            }
        });

        await generation.GenerateAsync(plan, new GenerationOptions { OutputDirectory = temp.Path });

        var ingress = await File.ReadAllTextAsync(Path.Combine(temp.Path, "kubernetes", "ingress.yaml"));
        ingress.Should().Contain("kind: Ingress");
        ingress.Should().NotContain("secretName:");
        File.Exists(Path.Combine(temp.Path, "kubernetes", "tls-secret.yaml")).Should().BeFalse();
        File.Exists(Path.Combine(temp.Path, "kubernetes", "certificate.yaml")).Should().BeFalse();
    }
}
