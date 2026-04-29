namespace Kubernator.Core.Strategy;

public enum TlsMode
{
    None,
    SelfSigned,
    CertManager,
    UserProvided
}

public sealed record ExposureOptions
{
    public required string PrimaryHostname { get; init; }
    public IReadOnlyList<string> AdditionalHostnames { get; init; } = [];
    public TlsMode TlsMode { get; init; } = TlsMode.SelfSigned;
    public string IngressClassName { get; init; } = "nginx";
    public string TlsSecretName { get; init; } = "tls-cert";
    public string? CertManagerIssuerName { get; init; }
    public string CertManagerIssuerKind { get; init; } = "ClusterIssuer";
    public string? UserCertificatePemPath { get; init; }
    public string? UserPrivateKeyPemPath { get; init; }
    public int? OverridePort { get; init; }
    public string Path { get; init; } = "/";
    public bool RedirectHttpToHttps { get; init; } = true;

    public IEnumerable<string> AllHostnames =>
        new[] { PrimaryHostname }.Concat(AdditionalHostnames);
}
