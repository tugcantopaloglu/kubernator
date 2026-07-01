using Kubernator.Core.ClusterProvisioning.Distros;
using Kubernator.Core.ClusterProvisioning.Os;
using Kubernator.Core.ClusterProvisioning.Ssh;
using Kubernator.Core.ClusterProvisioning.Topology;

namespace Kubernator.Core.ClusterProvisioning.Upgrade;

public sealed class ClusterUpgradePlanner
{
    private readonly INodeExecutor executor;
    private readonly IOsDetector osDetector;
    private readonly IReadOnlyDictionary<DistroKind, IClusterDistroProvisioner> provisioners;

    public ClusterUpgradePlanner(INodeExecutor executor, IOsDetector osDetector, IEnumerable<IClusterDistroProvisioner> provisioners)
    {
        this.executor = executor;
        this.osDetector = osDetector;
        this.provisioners = provisioners.ToDictionary(p => p.Kind);
    }

    public async Task<ClusterUpgradePlan> PlanAsync(ClusterTopology topology, string targetVersion, CancellationToken ct = default)
    {
        if (!provisioners.TryGetValue(topology.Distro, out var provisioner))
        {
            throw new NotSupportedException($"no provisioner registered for distro '{topology.Distro}'");
        }

        var ordered = topology.Nodes
            .Where(n => n.Role == NodeRole.Server)
            .Concat(topology.Nodes.Where(n => n.Role == NodeRole.Agent));

        var steps = new List<NodeUpgradeStep>();
        foreach (var node in ordered)
        {
            ct.ThrowIfCancellationRequested();
            var os = await osDetector.DetectAsync(node.Connection, executor, ct);
            var versionInfo = await provisioner.GetInstalledVersionAsync(node.Connection, executor, ct);
            var needsUpgrade = !versionInfo.Installed || DistroVersionComparer.NeedsUpgrade(versionInfo.Version, targetVersion);

            steps.Add(new NodeUpgradeStep
            {
                Node = node,
                CurrentVersion = versionInfo.Version,
                TargetVersion = targetVersion,
                NeedsUpgrade = needsUpgrade,
                Os = os
            });
        }

        return new ClusterUpgradePlan { Topology = topology, TargetVersion = targetVersion, Steps = steps };
    }
}
