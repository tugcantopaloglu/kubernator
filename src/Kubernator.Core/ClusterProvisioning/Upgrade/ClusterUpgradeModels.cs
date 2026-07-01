using Kubernator.Core.ClusterProvisioning.Os;
using Kubernator.Core.ClusterProvisioning.Topology;

namespace Kubernator.Core.ClusterProvisioning.Upgrade;

public sealed record NodeUpgradeStep
{
    public required NodeSpec Node { get; init; }
    public string? CurrentVersion { get; init; }
    public required string TargetVersion { get; init; }
    public required bool NeedsUpgrade { get; init; }
    public required OsFacts Os { get; init; }
}

public sealed record ClusterUpgradePlan
{
    public required ClusterTopology Topology { get; init; }
    public required string TargetVersion { get; init; }
    public required IReadOnlyList<NodeUpgradeStep> Steps { get; init; }
}

public sealed record ClusterUpgradeResult
{
    public required bool Ok { get; init; }
    public required IReadOnlyList<string> Errors { get; init; }
    public required IReadOnlyList<string> UpgradedNodes { get; init; }
    public required IReadOnlyList<string> SkippedNodes { get; init; }
}
