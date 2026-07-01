using Kubernator.Core.ClusterProvisioning.Topology;

namespace Kubernator.Core.ClusterProvisioning;

public sealed record ClusterProvisionOptions
{
    public required ClusterTopology Topology { get; init; }
    public bool AllowProduction { get; init; }
    public bool RegisterKubeconfigContext { get; init; } = true;
    public string? KubeconfigContextName { get; init; }
    public int MaxParallelAgents { get; init; } = 4;
    public string KubectlBinary { get; init; } = "kubectl";
}

public sealed record ClusterProvisionResult
{
    public required bool Ok { get; init; }
    public required IReadOnlyList<string> Errors { get; init; }
    public required IReadOnlyList<string> CompletedSteps { get; init; }
}
