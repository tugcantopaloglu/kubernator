using System.IO.Compression;

namespace Kubernator.Core.Packaging;

public sealed record BundleOptions
{
    public required string OutputBundlePath { get; init; }
    public required string ScratchDirectory { get; init; }
    public bool IncludeSbom { get; init; } = true;
    public string? KubernetesNamespace { get; init; }
    public int Replicas { get; init; } = 1;
    public bool BuildIfMissing { get; init; } = true;
    public bool KeepScratch { get; init; }
    public Generation.ScalingOptions? Scaling { get; init; }
    public CompressionLevel Compression { get; init; } = CompressionLevel.Optimal;
}

public sealed record BundleResult
{
    public required string BundlePath { get; init; }
    public required long BundleSizeBytes { get; init; }
    public required string BundleSha256 { get; init; }
    public required BundleManifest Manifest { get; init; }
}

public sealed record BundleVerificationResult
{
    public required bool Ok { get; init; }
    public required IReadOnlyList<string> Errors { get; init; }
    public required BundleManifest? Manifest { get; init; }
}
