using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Kubernator.Core.Tls;

public sealed record TlsMaterial
{
    public required string CertificatePem { get; init; }
    public required string PrivateKeyPem { get; init; }
}

public static class SelfSignedCertificateGenerator
{
    public static TlsMaterial Generate(string primaryHostname, IEnumerable<string> additionalHostnames)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(primaryHostname);

        using var rsa = RSA.Create(2048);
        var distinguishedName = $"CN={primaryHostname}";
        var request = new CertificateRequest(distinguishedName, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName(primaryHostname);
        foreach (var alt in additionalHostnames)
        {
            if (string.IsNullOrWhiteSpace(alt))
            {
                continue;
            }
            sanBuilder.AddDnsName(alt);
        }
        request.CertificateExtensions.Add(sanBuilder.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(certificateAuthority: false, hasPathLengthConstraint: false, pathLengthConstraint: 0, critical: true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
            critical: true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new("1.3.6.1.5.5.7.3.1") },
            critical: false));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, critical: false));

        var notBefore = DateTimeOffset.UtcNow.AddMinutes(-5);
        var notAfter = DateTimeOffset.UtcNow.AddYears(1);
        using var cert = request.CreateSelfSigned(notBefore, notAfter);

        var certPem = WrapPem("CERTIFICATE", cert.RawData);
        var keyPem = WrapPem("PRIVATE KEY", rsa.ExportPkcs8PrivateKey());

        return new TlsMaterial
        {
            CertificatePem = certPem,
            PrivateKeyPem = keyPem
        };
    }

    public static TlsMaterial LoadFromFiles(string certificatePath, string privateKeyPath)
    {
        return new TlsMaterial
        {
            CertificatePem = File.ReadAllText(certificatePath),
            PrivateKeyPem = File.ReadAllText(privateKeyPath)
        };
    }

    private static string WrapPem(string label, byte[] der)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("-----BEGIN ").Append(label).Append("-----\n");
        var b64 = Convert.ToBase64String(der);
        for (int i = 0; i < b64.Length; i += 64)
        {
            sb.Append(b64.AsSpan(i, Math.Min(64, b64.Length - i))).Append('\n');
        }
        sb.Append("-----END ").Append(label).Append("-----\n");
        return sb.ToString();
    }
}
