using Kubernator.Core.ClusterProvisioning.Distros;

namespace Kubernator.Core.ClusterProvisioning.Topology;

public sealed record ClusterTopologyValidationResult
{
    public required IReadOnlyList<string> Errors { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }

    public bool Ok => Errors.Count == 0;
}

public static class ClusterTopologyValidator
{
    public static ClusterTopologyValidationResult Validate(ClusterTopology topology)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (topology.Nodes.Count == 0)
        {
            errors.Add("topology has no nodes");
            return new ClusterTopologyValidationResult { Errors = errors, Warnings = warnings };
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in topology.Nodes)
        {
            if (string.IsNullOrWhiteSpace(node.Name))
            {
                errors.Add("every node must have a non-empty name");
                continue;
            }
            if (!names.Add(node.Name))
            {
                errors.Add($"duplicate node name: {node.Name}");
            }
        }

        var servers = topology.Nodes.Where(n => n.Role == NodeRole.Server).ToList();
        var agents = topology.Nodes.Where(n => n.Role == NodeRole.Agent).ToList();

        if (servers.Count == 0)
        {
            errors.Add("topology must include at least one server (control-plane) node");
        }

        var initServers = servers.Where(n => n.IsInitServer).ToList();
        if (initServers.Count == 0)
        {
            errors.Add("exactly one server node must have IsInitServer=true");
        }
        else if (initServers.Count > 1)
        {
            errors.Add($"only one server node may have IsInitServer=true (found {initServers.Count})");
        }

        if (servers.Count > 1 && topology.FixedRegistrationAddresses.Count == 0)
        {
            errors.Add("FixedRegistrationAddresses is required when more than one server node is configured (HA control plane needs a stable registration address)");
        }

        if (servers.Count > 1 && servers.Count % 2 == 0)
        {
            warnings.Add($"server count ({servers.Count}) is even; an odd number (1, 3, 5, ...) is recommended for etcd quorum safety");
        }

        if (agents.Count == 0)
        {
            warnings.Add("topology has no agent (worker) nodes; the control plane will need to run workloads itself");
        }

        if (topology.Distro == DistroKind.KubeadmNative && topology.CniPlugin is not ("flannel" or "calico"))
        {
            errors.Add($"kubeadm topologies must set cniPlugin to 'flannel' or 'calico' (got '{topology.CniPlugin}')");
        }

        if (!string.IsNullOrWhiteSpace(topology.PodCidr) && !System.Net.IPNetwork.TryParse(topology.PodCidr, out _))
        {
            errors.Add($"podCidr '{topology.PodCidr}' is not a valid CIDR (e.g. {ClusterNetworkDefaults.PodCidr})");
        }

        if (topology.CalicoEncapsulation is not ("bgp" or "vxlan"))
        {
            errors.Add($"calicoEncapsulation must be 'bgp' or 'vxlan' (got '{topology.CalicoEncapsulation}')");
        }
        else if (topology.CalicoEncapsulation == "vxlan" && topology.CniPlugin != "calico")
        {
            warnings.Add($"calicoEncapsulation 'vxlan' has no effect with cniPlugin '{topology.CniPlugin}'");
        }

        foreach (var node in topology.Nodes)
        {
            if (node.Connection.Mode == Ssh.NodeConnectionMode.Ssh && string.IsNullOrWhiteSpace(node.Connection.Host))
            {
                errors.Add($"node '{node.Name}' uses SSH connection mode but has no Host configured");
            }
        }

        return new ClusterTopologyValidationResult { Errors = errors, Warnings = warnings };
    }
}
