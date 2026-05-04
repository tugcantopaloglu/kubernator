namespace Kubernator.Core.Generation;

public sealed record GenerationOptions
{
    public required string OutputDirectory { get; init; }
    public string? Namespace { get; init; }
    public int Replicas { get; init; } = 1;
    public string? CpuRequest { get; init; } = "100m";
    public string? CpuLimit { get; init; } = "1000m";
    public string? MemoryRequest { get; init; } = "128Mi";
    public string? MemoryLimit { get; init; } = "512Mi";
    public bool OverwriteExisting { get; init; } = true;
    public ScalingOptions? Scaling { get; init; }
}

public sealed record GenerationResult
{
    public required string OutputDirectory { get; init; }
    public required IReadOnlyList<string> WrittenFiles { get; init; }
}
