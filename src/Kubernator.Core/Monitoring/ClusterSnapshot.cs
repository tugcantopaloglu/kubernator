namespace Kubernator.Core.Monitoring;

public sealed record ResourceQty
{
    public required string Cpu { get; init; }
    public required string Memory { get; init; }
    public string? Pods { get; init; }
}

public sealed record NodeCondition
{
    public required string Type { get; init; }
    public required string Status { get; init; }
    public string? Reason { get; init; }
    public string? Message { get; init; }
}

public sealed record NodeStatus
{
    public required string Name { get; init; }
    public required string Status { get; init; }
    public required IReadOnlyList<string> Roles { get; init; }
    public required string KubeletVersion { get; init; }
    public required string OsImage { get; init; }
    public required string Architecture { get; init; }
    public required ResourceQty Allocatable { get; init; }
    public ResourceQty? Usage { get; init; }
    public required IReadOnlyList<NodeCondition> Conditions { get; init; }
    public required TimeSpan Age { get; init; }
    public IReadOnlyDictionary<string, string> Labels { get; init; } = new Dictionary<string, string>();
}

public sealed record PodStatus
{
    public required string Namespace { get; init; }
    public required string Name { get; init; }
    public required string Phase { get; init; }
    public required string NodeName { get; init; }
    public required int Restarts { get; init; }
    public required int ContainersReady { get; init; }
    public required int ContainersTotal { get; init; }
    public required TimeSpan Age { get; init; }
    public ResourceQty? Usage { get; init; }
}

public sealed record IngressStatus
{
    public required string Namespace { get; init; }
    public required string Name { get; init; }
    public required string IngressClass { get; init; }
    public required IReadOnlyList<string> Hosts { get; init; }
    public required IReadOnlyList<string> Addresses { get; init; }
    public required IReadOnlyList<string> TlsHosts { get; init; }
    public required TimeSpan Age { get; init; }
}

public sealed record NetworkPolicyStatus
{
    public required string Namespace { get; init; }
    public required string Name { get; init; }
    public required string PodSelector { get; init; }
    public required IReadOnlyList<string> PolicyTypes { get; init; }
    public required TimeSpan Age { get; init; }
}

public sealed record ServiceStatus
{
    public required string Namespace { get; init; }
    public required string Name { get; init; }
    public required string Type { get; init; }
    public required string ClusterIp { get; init; }
    public required IReadOnlyList<string> ExternalIps { get; init; }
    public required IReadOnlyList<string> Ports { get; init; }
    public required TimeSpan Age { get; init; }
}

public sealed record ClusterSnapshot
{
    public required string Context { get; init; }
    public required DateTimeOffset CapturedAt { get; init; }
    public required IReadOnlyList<NodeStatus> Nodes { get; init; }
    public required IReadOnlyList<PodStatus> Pods { get; init; }
    public required IReadOnlyList<IngressStatus> Ingresses { get; init; }
    public required IReadOnlyList<NetworkPolicyStatus> NetworkPolicies { get; init; }
    public required IReadOnlyList<ServiceStatus> Services { get; init; }
    public bool MetricsServerAvailable { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public string? ApiVersion { get; init; }
    public int ReadyNodes => Nodes.Count(n => string.Equals(n.Status, "Ready", StringComparison.Ordinal));
    public int RunningPods => Pods.Count(p => string.Equals(p.Phase, "Running", StringComparison.Ordinal));
    public int FailedPods => Pods.Count(p => string.Equals(p.Phase, "Failed", StringComparison.Ordinal));
}
