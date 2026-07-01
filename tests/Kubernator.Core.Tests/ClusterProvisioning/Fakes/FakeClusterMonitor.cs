using Kubernator.Core.Monitoring;

namespace Kubernator.Core.Tests.ClusterProvisioning.Fakes;

internal sealed class FakeClusterMonitor : IClusterMonitor
{
    public ClusterSnapshot Snapshot { get; set; } = new()
    {
        Context = "test",
        CapturedAt = DateTimeOffset.UtcNow,
        Nodes = [],
        Pods = [],
        Ingresses = [],
        NetworkPolicies = [],
        Services = []
    };

    public Task<ClusterSnapshot> GetSnapshotAsync(ClusterMonitorOptions options, CancellationToken ct = default) =>
        Task.FromResult(Snapshot);
}
