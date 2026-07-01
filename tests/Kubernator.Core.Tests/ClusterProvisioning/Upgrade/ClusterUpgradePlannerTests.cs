using Kubernator.Core.ClusterProvisioning.Distros;
using Kubernator.Core.ClusterProvisioning.Os;
using Kubernator.Core.ClusterProvisioning.Ssh;
using Kubernator.Core.ClusterProvisioning.Topology;
using Kubernator.Core.ClusterProvisioning.Upgrade;
using Kubernator.Core.Tests.ClusterProvisioning.Fakes;

namespace Kubernator.Core.Tests.ClusterProvisioning.Upgrade;

public sealed class ClusterUpgradePlannerTests
{
    private static NodeSpec Server(string name, bool isInit = false) => new()
    {
        Name = name,
        Role = NodeRole.Server,
        Connection = new NodeConnection { Mode = NodeConnectionMode.Ssh, Host = name },
        IsInitServer = isInit
    };

    private static NodeSpec Agent(string name) => new()
    {
        Name = name,
        Role = NodeRole.Agent,
        Connection = new NodeConnection { Mode = NodeConnectionMode.Ssh, Host = name }
    };

    [Fact]
    public async Task Nodes_already_at_target_version_are_marked_as_not_needing_upgrade()
    {
        var executor = new RecordingNodeExecutor();
        var osDetector = new FakeOsDetector();
        var provisioner = new FakeClusterDistroProvisioner
        {
            VersionResponder = _ => new NodeVersionInfo { Installed = true, Version = "v1.30.4+rke2r1", Role = NodeRole.Server }
        };
        var planner = new ClusterUpgradePlanner(executor, osDetector, [provisioner]);

        var topology = new ClusterTopology
        {
            ClusterName = "demo",
            Distro = DistroKind.Rke2,
            Version = "v1.30.4+rke2r1",
            Nodes = [Server("m1", isInit: true)],
            LocalArtifactBundlePath = "./bundle"
        };

        var plan = await planner.PlanAsync(topology, "v1.30.4+rke2r1");

        plan.Steps.Should().ContainSingle();
        plan.Steps[0].NeedsUpgrade.Should().BeFalse();
        plan.Steps[0].CurrentVersion.Should().Be("v1.30.4+rke2r1");
    }

    [Fact]
    public async Task Nodes_on_an_older_version_need_upgrade()
    {
        var executor = new RecordingNodeExecutor();
        var osDetector = new FakeOsDetector();
        var provisioner = new FakeClusterDistroProvisioner
        {
            VersionResponder = _ => new NodeVersionInfo { Installed = true, Version = "v1.30.3+rke2r1", Role = NodeRole.Server }
        };
        var planner = new ClusterUpgradePlanner(executor, osDetector, [provisioner]);

        var topology = new ClusterTopology
        {
            ClusterName = "demo",
            Distro = DistroKind.Rke2,
            Version = "v1.30.3+rke2r1",
            Nodes = [Server("m1", isInit: true)],
            LocalArtifactBundlePath = "./bundle"
        };

        var plan = await planner.PlanAsync(topology, "v1.30.4+rke2r1");

        plan.Steps.Should().ContainSingle();
        plan.Steps[0].NeedsUpgrade.Should().BeTrue();
    }

    [Fact]
    public async Task Same_core_different_build_suffix_still_needs_upgrade()
    {
        var executor = new RecordingNodeExecutor();
        var osDetector = new FakeOsDetector();
        var provisioner = new FakeClusterDistroProvisioner
        {
            VersionResponder = _ => new NodeVersionInfo { Installed = true, Version = "v1.30.4+rke2r1", Role = NodeRole.Server }
        };
        var planner = new ClusterUpgradePlanner(executor, osDetector, [provisioner]);

        var topology = new ClusterTopology
        {
            ClusterName = "demo",
            Distro = DistroKind.Rke2,
            Version = "v1.30.4+rke2r1",
            Nodes = [Server("m1", isInit: true)],
            LocalArtifactBundlePath = "./bundle"
        };

        var plan = await planner.PlanAsync(topology, "v1.30.4+rke2r2");

        plan.Steps[0].NeedsUpgrade.Should().BeTrue();
    }

    [Fact]
    public async Task Uninstalled_nodes_need_upgrade()
    {
        var executor = new RecordingNodeExecutor();
        var osDetector = new FakeOsDetector();
        var provisioner = new FakeClusterDistroProvisioner
        {
            VersionResponder = _ => new NodeVersionInfo { Installed = false }
        };
        var planner = new ClusterUpgradePlanner(executor, osDetector, [provisioner]);

        var topology = new ClusterTopology
        {
            ClusterName = "demo",
            Distro = DistroKind.Rke2,
            Version = "v1.30.4+rke2r1",
            Nodes = [Server("m1", isInit: true)],
            LocalArtifactBundlePath = "./bundle"
        };

        var plan = await planner.PlanAsync(topology, "v1.30.4+rke2r1");

        plan.Steps[0].NeedsUpgrade.Should().BeTrue();
        plan.Steps[0].CurrentVersion.Should().BeNull();
    }

    [Fact]
    public async Task Plan_orders_servers_before_agents()
    {
        var executor = new RecordingNodeExecutor();
        var osDetector = new FakeOsDetector();
        var provisioner = new FakeClusterDistroProvisioner
        {
            VersionResponder = _ => new NodeVersionInfo { Installed = false }
        };
        var planner = new ClusterUpgradePlanner(executor, osDetector, [provisioner]);

        var topology = new ClusterTopology
        {
            ClusterName = "demo",
            Distro = DistroKind.Rke2,
            Version = "v1",
            Nodes = [Agent("w1"), Server("m1", isInit: true), Agent("w2")],
            LocalArtifactBundlePath = "./bundle"
        };

        var plan = await planner.PlanAsync(topology, "v2");

        plan.Steps.Select(s => s.Node.Name).Should().ContainInOrder("m1", "w1", "w2");
    }
}
