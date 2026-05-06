namespace Kubernator.Core.Monitoring;

public sealed record ClusterMonitorOptions
{
    public required string Context { get; init; }
    public string? Namespace { get; init; }
    public string KubectlBinary { get; init; } = "kubectl";
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(20);
    public bool IncludeMetrics { get; init; } = true;
    public bool IncludePods { get; init; } = true;
    public bool IncludeIngress { get; init; } = true;
    public bool IncludeNetworkPolicies { get; init; } = true;
    public bool IncludeServices { get; init; } = true;
}

public interface IClusterMonitor
{
    Task<ClusterSnapshot> GetSnapshotAsync(ClusterMonitorOptions options, CancellationToken ct = default);
}
