using Kubernator.Core.Generation;
using Kubernator.Core.Strategy;

namespace Kubernator.Web.Api;

public sealed record GenerateRequest
{
    public required string Path { get; init; }
    public string? OutputDirectory { get; init; }
    public string? ImageName { get; init; }
    public string? ImageTag { get; init; }
    public string? Namespace { get; init; }
    public int Replicas { get; init; } = 1;
    public string? CpuRequest { get; init; }
    public string? CpuLimit { get; init; }
    public string? MemoryRequest { get; init; }
    public string? MemoryLimit { get; init; }
    public ScalingOptionsDto? Scaling { get; init; }
    public ExposureOptionsDto? Exposure { get; init; }
    public bool ReturnDownloadToken { get; init; }
}

public sealed record GenerateResponse
{
    public required string OutputDirectory { get; init; }
    public required IReadOnlyList<string> WrittenFiles { get; init; }
    public required string ImageReference { get; init; }
    public required string BaseImage { get; init; }
    public string? DownloadToken { get; init; }
    public string? DownloadUrl { get; init; }
}

public sealed record ScalingOptionsDto
{
    public int? HpaMinReplicas { get; init; }
    public int? HpaMaxReplicas { get; init; }
    public int? HpaTargetCpuUtilization { get; init; }
    public int? HpaTargetMemoryUtilization { get; init; }
    public int? PdbMinAvailable { get; init; }
    public int? PdbMaxUnavailable { get; init; }
    public string? PdbMinAvailablePercent { get; init; }
    public string? PdbMaxUnavailablePercent { get; init; }

    public ScalingOptions? ToCore()
    {
        if (HpaMinReplicas is null && HpaMaxReplicas is null
            && HpaTargetCpuUtilization is null && HpaTargetMemoryUtilization is null
            && PdbMinAvailable is null && PdbMaxUnavailable is null
            && string.IsNullOrEmpty(PdbMinAvailablePercent) && string.IsNullOrEmpty(PdbMaxUnavailablePercent))
        {
            return null;
        }
        return new ScalingOptions
        {
            HpaMinReplicas = HpaMinReplicas,
            HpaMaxReplicas = HpaMaxReplicas,
            HpaTargetCpuUtilization = HpaTargetCpuUtilization,
            HpaTargetMemoryUtilization = HpaTargetMemoryUtilization,
            PdbMinAvailable = PdbMinAvailable,
            PdbMaxUnavailable = PdbMaxUnavailable,
            PdbMinAvailablePercent = PdbMinAvailablePercent,
            PdbMaxUnavailablePercent = PdbMaxUnavailablePercent
        };
    }
}

public sealed record ExposureOptionsDto
{
    public required string PrimaryHostname { get; init; }
    public IReadOnlyList<string>? AdditionalHostnames { get; init; }
    public string? TlsMode { get; init; }
    public string? IngressClassName { get; init; }
    public string? TlsSecretName { get; init; }
    public string? CertManagerIssuerName { get; init; }
    public string? CertManagerIssuerKind { get; init; }
    public int? OverridePort { get; init; }
    public string? Path { get; init; }
    public bool? RedirectHttpToHttps { get; init; }

    public ExposureOptions ToCore()
    {
        var mode = TlsMode switch
        {
            null => Core.Strategy.TlsMode.SelfSigned,
            "" => Core.Strategy.TlsMode.SelfSigned,
            _ when Enum.TryParse<TlsMode>(TlsMode, true, out var parsed) => parsed,
            _ => throw ApiException.BadRequest("invalid tlsMode", $"expected one of: None, SelfSigned, CertManager, UserProvided")
        };
        return new ExposureOptions
        {
            PrimaryHostname = PrimaryHostname,
            AdditionalHostnames = AdditionalHostnames ?? Array.Empty<string>(),
            TlsMode = mode,
            IngressClassName = IngressClassName ?? "nginx",
            TlsSecretName = TlsSecretName ?? "tls-cert",
            CertManagerIssuerName = CertManagerIssuerName,
            CertManagerIssuerKind = CertManagerIssuerKind ?? "ClusterIssuer",
            OverridePort = OverridePort,
            Path = Path ?? "/",
            RedirectHttpToHttps = RedirectHttpToHttps ?? true
        };
    }
}

public sealed record HelmRequest
{
    public required string Path { get; init; }
    public string? OutputDirectory { get; init; }
    public string? ChartName { get; init; }
    public string? ChartVersion { get; init; }
    public string? Description { get; init; }
    public string? Namespace { get; init; }
    public string? ImageName { get; init; }
    public string? ImageTag { get; init; }
    public int Replicas { get; init; } = 1;
    public ScalingOptionsDto? Scaling { get; init; }
    public ExposureOptionsDto? Exposure { get; init; }
    public bool ReturnDownloadToken { get; init; }
}

public sealed record HelmResponse
{
    public required string ChartDirectory { get; init; }
    public required IReadOnlyList<string> WrittenFiles { get; init; }
    public string? DownloadToken { get; init; }
    public string? DownloadUrl { get; init; }
}

public sealed record KustomizeRequest
{
    public required string Path { get; init; }
    public string? OutputDirectory { get; init; }
    public string? Namespace { get; init; }
    public IReadOnlyList<string>? Overlays { get; init; }
    public string? ImageName { get; init; }
    public string? ImageTag { get; init; }
    public int Replicas { get; init; } = 1;
    public ScalingOptionsDto? Scaling { get; init; }
    public ExposureOptionsDto? Exposure { get; init; }
    public bool ReturnDownloadToken { get; init; }
}

public sealed record KustomizeResponse
{
    public required string BaseDirectory { get; init; }
    public required IReadOnlyList<string> WrittenFiles { get; init; }
    public string? DownloadToken { get; init; }
    public string? DownloadUrl { get; init; }
}

public sealed record GitOpsRequest
{
    public required string Path { get; init; }
    public required string RepoUrl { get; init; }
    public string? OutputDirectory { get; init; }
    public string? TargetRevision { get; init; }
    public string? SourcePath { get; init; }
    public string? SourceKind { get; init; }
    public string? DestinationServer { get; init; }
    public string? DestinationNamespace { get; init; }
    public string? ArgoNamespace { get; init; }
    public string? ApplicationName { get; init; }
    public string? ProjectName { get; init; }
    public bool? AutomatedSync { get; init; }
    public bool? SelfHeal { get; init; }
    public bool? Prune { get; init; }
    public bool? CreateNamespace { get; init; }
    public string? ImageName { get; init; }
    public string? ImageTag { get; init; }
    public bool ReturnDownloadToken { get; init; }
}

public sealed record GitOpsResponse
{
    public required string OutputDirectory { get; init; }
    public required IReadOnlyList<string> WrittenFiles { get; init; }
    public string? DownloadToken { get; init; }
    public string? DownloadUrl { get; init; }
}

public sealed record PipelineRequest
{
    public required string Path { get; init; }
    public required string Target { get; init; }
    public string? OutputDirectory { get; init; }
    public string? Registry { get; init; }
    public string? Namespace { get; init; }
    public string? PublishPath { get; init; }
    public string? BundleArtifactName { get; init; }
    public bool? SignBundle { get; init; }
    public bool? RunVerify { get; init; }
    public string? ImageName { get; init; }
    public string? ImageTag { get; init; }
    public bool ReturnDownloadToken { get; init; }
}

public sealed record PipelineResponse
{
    public required string Target { get; init; }
    public required IReadOnlyList<string> WrittenFiles { get; init; }
    public string? DownloadToken { get; init; }
    public string? DownloadUrl { get; init; }
}

public sealed record TlsRotateRequest
{
    public required string SecretName { get; init; }
    public required string Hostname { get; init; }
    public string? OutputDirectory { get; init; }
    public string? Namespace { get; init; }
    public string? Schedule { get; init; }
    public int? DaysValid { get; init; }
    public IReadOnlyList<string>? AdditionalHostnames { get; init; }
    public string? ServiceAccountName { get; init; }
    public string? CronJobName { get; init; }
    public bool ReturnDownloadToken { get; init; }
}

public sealed record TlsRotateResponse
{
    public required string OutputDirectory { get; init; }
    public required IReadOnlyList<string> WrittenFiles { get; init; }
    public required string ResolvedServiceAccountName { get; init; }
    public required string ResolvedCronJobName { get; init; }
    public string? DownloadToken { get; init; }
    public string? DownloadUrl { get; init; }
}

public sealed record ScanRequest
{
    public required string Path { get; init; }
    public string? Ecosystem { get; init; }
    public string? MinSeverity { get; init; }
    public IReadOnlyList<string>? IgnoreIds { get; init; }
    public bool IncludeUnknownVersions { get; init; }
}

public sealed record ScanResponse
{
    public required IReadOnlyList<VulnerabilityFindingDto> Findings { get; init; }
    public required int PackagesScanned { get; init; }
    public required string Ecosystem { get; init; }
    public required bool DatabasePresent { get; init; }
    public DateTimeOffset? DatabaseUpdatedAt { get; init; }
}

public sealed record VulnerabilityFindingDto
{
    public required string PackageName { get; init; }
    public required string PackageVersion { get; init; }
    public required string Ecosystem { get; init; }
    public required string VulnerabilityId { get; init; }
    public required string Severity { get; init; }
    public required string SeverityRaw { get; init; }
    public string? Summary { get; init; }
    public string? FixedIn { get; init; }
    public IReadOnlyList<string> References { get; init; } = Array.Empty<string>();

    public static VulnerabilityFindingDto From(Core.Vulnerabilities.VulnerabilityFinding f) => new()
    {
        PackageName = f.PackageName,
        PackageVersion = f.PackageVersion,
        Ecosystem = f.Ecosystem,
        VulnerabilityId = f.VulnerabilityId,
        Severity = f.Severity.ToString(),
        SeverityRaw = f.SeverityRaw,
        Summary = f.Summary,
        FixedIn = f.FixedIn,
        References = f.References
    };
}

public sealed record VulnDbStatusResponse
{
    public required bool Present { get; init; }
    public string? SchemaVersion { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
    public IReadOnlyList<VulnDbEcosystemDto> Ecosystems { get; init; } = Array.Empty<VulnDbEcosystemDto>();
}

public sealed record VulnDbEcosystemDto
{
    public required string Name { get; init; }
    public required int PackageCount { get; init; }
    public required int VulnerabilityCount { get; init; }
    public DateTimeOffset? LastImportedAt { get; init; }
}

public sealed record VaultEntryDto
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Kind { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required string FileName { get; init; }
    public string? Fingerprint { get; init; }
    public required bool Encrypted { get; init; }

    public static VaultEntryDto From(Core.Vault.VaultEntry v) => new()
    {
        Id = v.Id,
        Name = v.Name,
        Kind = v.Kind.ToString(),
        CreatedAt = v.CreatedAt,
        FileName = v.FileName,
        Fingerprint = v.Fingerprint,
        Encrypted = v.Encrypted
    };
}

public sealed record VaultListResponse
{
    public required IReadOnlyList<VaultEntryDto> Entries { get; init; }
}

public sealed record BundleVerifyRequest
{
    public required string BundlePath { get; init; }
}

public sealed record BundleVerifyResponse
{
    public required bool Ok { get; init; }
    public required IReadOnlyList<string> Errors { get; init; }
    public BundleManifestDto? Manifest { get; init; }
}

public sealed record BundleManifestDto
{
    public required string SchemaVersion { get; init; }
    public required string Tool { get; init; }
    public required string ToolVersion { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required string AppName { get; init; }
    public required string AppKind { get; init; }
    public required string KubernetesNamespace { get; init; }
    public required IReadOnlyList<string> Images { get; init; }
    public required IReadOnlyList<string> Files { get; init; }

    public static BundleManifestDto From(Core.Packaging.BundleManifest m) => new()
    {
        SchemaVersion = m.SchemaVersion,
        Tool = m.Tool,
        ToolVersion = m.ToolVersion,
        CreatedAt = m.CreatedAt,
        AppName = m.App.Name,
        AppKind = m.App.Kind,
        KubernetesNamespace = m.KubernetesNamespace,
        Images = m.Images.Select(i => i.Reference).ToArray(),
        Files = m.Files.Select(f => f.RelativePath).ToArray()
    };
}

public sealed record BaseImageInfoResponse
{
    public required IReadOnlyList<string> AllowedRegistries { get; init; }
}
