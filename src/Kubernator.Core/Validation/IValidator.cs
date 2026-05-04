namespace Kubernator.Core.Validation;

public sealed record ValidationOptions
{
    public required string ManifestsDirectory { get; init; }
    public required string ImageReference { get; init; }
    public required string DeploymentName { get; init; }
    public string ClusterName { get; init; } = "kubernator-test";
    public string Namespace { get; init; } = "default";
    public TimeSpan ReadyTimeout { get; init; } = TimeSpan.FromMinutes(2);
    public bool DeleteClusterOnComplete { get; init; } = true;
    public bool ReuseExistingCluster { get; init; }
    public string? HttpProbePath { get; init; }
    public int? HttpProbePort { get; init; }
    public string KindBinary { get; init; } = "kind";
    public string KubectlBinary { get; init; } = "kubectl";
    public string? KubeContext { get; init; }
}

public sealed record ValidationStep
{
    public required string Name { get; init; }
    public required bool Ok { get; init; }
    public required TimeSpan Duration { get; init; }
    public string? Error { get; init; }
    public string? Output { get; init; }
}

public sealed record ValidationResult
{
    public required bool Ok { get; init; }
    public required IReadOnlyList<ValidationStep> Steps { get; init; }
    public required string ClusterName { get; init; }
    public required bool ClusterStillRunning { get; init; }
}

public interface IValidator
{
    Task<ValidationResult> ValidateAsync(
        ValidationOptions options,
        IProgress<string>? progress = null,
        CancellationToken ct = default);
}
