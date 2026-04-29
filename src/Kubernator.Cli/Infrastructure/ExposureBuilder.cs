using Kubernator.Core.Strategy;

namespace Kubernator.Cli.Infrastructure;

internal static class ExposureBuilder
{
    public static ExposureOptions? Build(
        string? hostname,
        IEnumerable<string>? extraHosts,
        string? tlsModeRaw,
        string? ingressClass,
        string? tlsSecretName,
        string? certIssuer,
        string? issuerKind,
        string? certFile,
        string? keyFile,
        string? path,
        bool noHttpsRedirect,
        int? overridePort)
    {
        if (string.IsNullOrEmpty(hostname))
        {
            return null;
        }

        var mode = ParseTlsMode(tlsModeRaw);

        if (mode == TlsMode.UserProvided)
        {
            if (string.IsNullOrEmpty(certFile) || string.IsNullOrEmpty(keyFile))
            {
                throw new InvalidOperationException("--tls user requires --cert-file and --key-file");
            }
        }
        if (mode == TlsMode.CertManager && string.IsNullOrEmpty(certIssuer))
        {
            throw new InvalidOperationException("--tls cert-manager requires --cert-issuer");
        }

        return new ExposureOptions
        {
            PrimaryHostname = hostname,
            AdditionalHostnames = extraHosts?.Where(h => !string.IsNullOrWhiteSpace(h)).ToArray() ?? [],
            TlsMode = mode,
            IngressClassName = ingressClass ?? "nginx",
            TlsSecretName = tlsSecretName ?? "tls-cert",
            CertManagerIssuerName = certIssuer,
            CertManagerIssuerKind = issuerKind ?? "ClusterIssuer",
            UserCertificatePemPath = certFile,
            UserPrivateKeyPemPath = keyFile,
            OverridePort = overridePort,
            Path = path ?? "/",
            RedirectHttpToHttps = !noHttpsRedirect
        };
    }

    private static TlsMode ParseTlsMode(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return TlsMode.SelfSigned;
        }
        return raw.ToLowerInvariant() switch
        {
            "none" or "off" => TlsMode.None,
            "self-signed" or "self" => TlsMode.SelfSigned,
            "cert-manager" or "certmanager" or "acme" => TlsMode.CertManager,
            "user" or "user-provided" or "byo" => TlsMode.UserProvided,
            _ => throw new InvalidOperationException($"unknown --tls value: {raw} (use none|self-signed|cert-manager|user)")
        };
    }
}
