using Kubernator.Core.Generation;
using Kubernator.Core.Strategy;

namespace Kubernator.Core.Kustomize;

public sealed record KustomizeOptions
{
    public required string OutputDirectory { get; init; }
    public string? BaseNamespace { get; init; }
    public IReadOnlyList<string> Overlays { get; init; } = ["production"];
    public ScalingOptions? Scaling { get; init; }
    public ExposureOptions? Exposure { get; init; }
    public int Replicas { get; init; } = 1;
    public string CpuRequest { get; init; } = "100m";
    public string CpuLimit { get; init; } = "1000m";
    public string MemoryRequest { get; init; } = "128Mi";
    public string MemoryLimit { get; init; } = "512Mi";
}

public sealed record KustomizeResult
{
    public required string BaseDirectory { get; init; }
    public required IReadOnlyList<string> WrittenFiles { get; init; }
}
