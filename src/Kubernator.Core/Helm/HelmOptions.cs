using Kubernator.Core.Generation;
using Kubernator.Core.Strategy;

namespace Kubernator.Core.Helm;

public sealed record HelmOptions
{
    public required string OutputDirectory { get; init; }
    public string? ChartName { get; init; }
    public string ChartVersion { get; init; } = "0.1.0";
    public string? Description { get; init; }
    public bool Package { get; init; }
    public ScalingOptions? Scaling { get; init; }
    public ExposureOptions? Exposure { get; init; }
    public string? Namespace { get; init; }
    public int Replicas { get; init; } = 1;
    public string CpuRequest { get; init; } = "100m";
    public string CpuLimit { get; init; } = "1000m";
    public string MemoryRequest { get; init; } = "128Mi";
    public string MemoryLimit { get; init; } = "512Mi";
}

public sealed record HelmGenerationResult
{
    public required string ChartDirectory { get; init; }
    public required IReadOnlyList<string> WrittenFiles { get; init; }
    public string? PackageFile { get; init; }
}
