using Kubernator.Core.ClusterProvisioning.Distros;

namespace Kubernator.Core.ClusterProvisioning.Artifacts;

public sealed record ClusterArtifactPullOptions
{
    public required string OutputDirectory { get; init; }
    public required DistroKind Distro { get; init; }
    public required string Version { get; init; }
    public required IReadOnlyList<string> Architectures { get; init; }
    public bool IncludeKubectl { get; init; } = true;
    public bool IncludeHelm { get; init; }
    public bool IncludeK9s { get; init; }
    public string HelmVersion { get; init; } = "v3.16.2";
    public string K9sVersion { get; init; } = "v0.32.5";
    public bool IncludeSelinuxPolicy { get; init; }
    public string? SelinuxPolicyVersion { get; init; }
}

public sealed record ClusterArtifactEntry
{
    public required string Kind { get; init; }
    public string? Arch { get; init; }
    public required string RelativePath { get; init; }
    public required long SizeBytes { get; init; }
    public required string Sha256 { get; init; }
}

public sealed record ClusterArtifactManifest
{
    public required string Distro { get; init; }
    public required string Version { get; init; }
    public required IReadOnlyList<ClusterArtifactEntry> Entries { get; init; }
}
