using Kubernator.Core.ClusterProvisioning;
using Kubernator.Core.ClusterProvisioning.Artifacts;
using Kubernator.Core.ClusterProvisioning.Upgrade;

namespace Kubernator.Web.Api;

public sealed record ClusterPullRequest
{
    public required string OutputDirectory { get; init; }
    public string Distro { get; init; } = "rke2";
    public required string Version { get; init; }
    public required IReadOnlyList<string> Architectures { get; init; }
    public bool IncludeKubectl { get; init; } = true;
    public bool IncludeHelm { get; init; }
    public bool IncludeK9s { get; init; }
    public string? HelmVersion { get; init; }
    public string? K9sVersion { get; init; }
    public bool IncludeSelinuxPolicy { get; init; }
    public string? SelinuxPolicyVersion { get; init; }
    public string? PackArchivePath { get; init; }
}

public sealed record ClusterArtifactEntryDto
{
    public required string Kind { get; init; }
    public string? Arch { get; init; }
    public required string RelativePath { get; init; }
    public required long SizeBytes { get; init; }
    public required string Sha256 { get; init; }

    public static ClusterArtifactEntryDto From(ClusterArtifactEntry e) => new()
    {
        Kind = e.Kind,
        Arch = e.Arch,
        RelativePath = e.RelativePath,
        SizeBytes = e.SizeBytes,
        Sha256 = e.Sha256
    };
}

public sealed record ClusterArtifactManifestDto
{
    public required string Distro { get; init; }
    public required string Version { get; init; }
    public required IReadOnlyList<ClusterArtifactEntryDto> Entries { get; init; }
    public string? PackedArchivePath { get; init; }

    public static ClusterArtifactManifestDto From(ClusterArtifactManifest m, string? packedArchivePath = null) => new()
    {
        Distro = m.Distro,
        Version = m.Version,
        Entries = m.Entries.Select(ClusterArtifactEntryDto.From).ToArray(),
        PackedArchivePath = packedArchivePath
    };
}

public sealed record ClusterTrustHostRequest
{
    public required string Host { get; init; }
    public int Port { get; init; } = 22;
    public required string Username { get; init; }
    public bool Confirm { get; init; }
}

public sealed record ClusterTrustHostResponse
{
    public required string Host { get; init; }
    public required int Port { get; init; }
    public required string Fingerprint { get; init; }
    public required bool Trusted { get; init; }
}

public sealed record ClusterTopologyRequest
{
    public required string TopologyPath { get; init; }
    public bool AllowProduction { get; init; }
}

public sealed record ClusterUpgradeRequest
{
    public required string TopologyPath { get; init; }
    public required string ToVersion { get; init; }
    public bool AllowProduction { get; init; }
}

public sealed record ClusterStatusRequest
{
    public required string TopologyPath { get; init; }
}

public sealed record ClusterDiscoverRequest
{
    public string? Context { get; init; }
    public required string ClusterName { get; init; }
    public string Distro { get; init; } = "rke2";
    public required string Version { get; init; }
    public string? LocalArtifactBundlePath { get; init; }
    public required string SshUsername { get; init; }
    public string? SshPrivateKeyVaultId { get; init; }
    public string? SshPrivateKeyPath { get; init; }
    public int SshPort { get; init; } = 22;
    public IReadOnlyList<string> FixedRegistrationAddresses { get; init; } = [];

    /// <summary>Optional path to write the discovered topology JSON to, mirroring the CLI's <c>--output</c>.</summary>
    public string? OutputPath { get; init; }
}

public sealed record ClusterDiscoverNodeDto
{
    public required string Name { get; init; }
    public required string Role { get; init; }
    public string? Host { get; init; }
    public required bool IsInitServer { get; init; }
}

public sealed record ClusterDiscoverResponse
{
    public required string ClusterName { get; init; }
    public required bool TopologyOk { get; init; }
    public required IReadOnlyList<string> Errors { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required IReadOnlyList<ClusterDiscoverNodeDto> Nodes { get; init; }

    /// <summary>The discovered topology serialized in the same canonical form the CLI writes.</summary>
    public required string TopologyJson { get; init; }

    /// <summary>Absolute path the topology was written to, when <c>OutputPath</c> was supplied.</summary>
    public string? WrittenTo { get; init; }
}

public sealed record ClusterInstallResultDto
{
    public required bool Ok { get; init; }
    public required IReadOnlyList<string> Errors { get; init; }
    public required IReadOnlyList<string> CompletedSteps { get; init; }

    public static ClusterInstallResultDto From(ClusterProvisionResult r) => new()
    {
        Ok = r.Ok,
        Errors = r.Errors,
        CompletedSteps = r.CompletedSteps
    };
}

public sealed record ClusterUpgradeResultDto
{
    public required bool Ok { get; init; }
    public required IReadOnlyList<string> Errors { get; init; }
    public required IReadOnlyList<string> UpgradedNodes { get; init; }
    public required IReadOnlyList<string> SkippedNodes { get; init; }

    public static ClusterUpgradeResultDto From(ClusterUpgradeResult r) => new()
    {
        Ok = r.Ok,
        Errors = r.Errors,
        UpgradedNodes = r.UpgradedNodes,
        SkippedNodes = r.SkippedNodes
    };
}

public sealed record ClusterNodeStatusDto
{
    public required string Name { get; init; }
    public required string Role { get; init; }
    public required string Os { get; init; }
    public string? CurrentVersion { get; init; }
    public required string TargetVersion { get; init; }
    public required bool NeedsUpgrade { get; init; }
}

public sealed record ClusterStatusResponse
{
    public required string ClusterName { get; init; }
    public required bool TopologyOk { get; init; }
    public required IReadOnlyList<string> Errors { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required IReadOnlyList<ClusterNodeStatusDto> Nodes { get; init; }
}
