using Kubernator.Core.ClusterProvisioning;
using Kubernator.Core.ClusterProvisioning.Distros;
using Kubernator.Core.ClusterProvisioning.Ssh;
using Kubernator.Core.ClusterProvisioning.Topology;
using Kubernator.Core.ClusterProvisioning.Upgrade;
using Kubernator.Core.Tests.ClusterProvisioning.Fakes;

namespace Kubernator.Core.Tests.ClusterProvisioning;

public sealed class ClusterProvisioningServiceTests
{
    private static NodeSpec Server(string name, bool isInit = false) => new()
    {
        Name = name,
        Role = NodeRole.Server,
        Connection = new NodeConnection { Mode = NodeConnectionMode.Ssh, Host = name },
        AdvertiseAddress = name,
        IsInitServer = isInit
    };

    private static NodeSpec Agent(string name) => new()
    {
        Name = name,
        Role = NodeRole.Agent,
        Connection = new NodeConnection { Mode = NodeConnectionMode.Ssh, Host = name }
    };

    private static (ClusterProvisioningService Service, RecordingNodeExecutor Executor, FakeClusterDistroProvisioner Provisioner, FakeClusterApplier Applier, FakeClusterMonitor Monitor)
        BuildService()
    {
        var executor = new RecordingNodeExecutor();
        var osDetector = new FakeOsDetector();
        var provisioner = new FakeClusterDistroProvisioner();
        var monitor = new FakeClusterMonitor();
        var applier = new FakeClusterApplier();
        var processRunner = new RecordingProcessRunner();
        var upgradePlanner = new ClusterUpgradePlanner(executor, osDetector, [provisioner]);

        var service = new ClusterProvisioningService(executor, osDetector, [provisioner], monitor, applier, processRunner, upgradePlanner);
        return (service, executor, provisioner, applier, monitor);
    }

    [Fact]
    public async Task Install_refuses_production_cluster_name_without_AllowProduction()
    {
        var (service, executor, provisioner, _, _) = BuildService();
        var topology = new ClusterTopology
        {
            ClusterName = "prod-eu",
            Distro = DistroKind.Rke2,
            Version = "v1",
            Nodes = [Server("m1", isInit: true)],
            LocalArtifactBundlePath = "./bundle"
        };

        var result = await service.InstallAsync(new ClusterProvisionOptions { Topology = topology });

        result.Ok.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Contains("looks like a production cluster"));
        executor.ExecCalls.Should().BeEmpty();
        provisioner.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Install_ha_topology_joins_masters_sequentially_then_agents()
    {
        var (service, executor, provisioner, applier, _) = BuildService();
        var topology = new ClusterTopology
        {
            ClusterName = "demo",
            Distro = DistroKind.Rke2,
            Version = "v1.30.4+rke2r1",
            Nodes = [Server("m1", isInit: true), Server("m2"), Server("m3"), Agent("w1"), Agent("w2")],
            FixedRegistrationAddresses = ["lb.internal"],
            LocalArtifactBundlePath = "./bundle"
        };

        var result = await service.InstallAsync(new ClusterProvisionOptions { Topology = topology });

        result.Ok.Should().BeTrue(because: string.Join("; ", result.Errors));

        var events = provisioner.Events;
        events.Should().Contain("BootstrapFirstServer:m1");
        events.Should().Contain("JoinAdditionalServer:m2");
        events.Should().Contain("JoinAdditionalServer:m3");
        events.Should().Contain("JoinAgent:w1");
        events.Should().Contain("JoinAgent:w2");

        var bootstrapIdx = events.IndexOf("BootstrapFirstServer:m1");
        var m2Idx = events.IndexOf("JoinAdditionalServer:m2");
        var m3Idx = events.IndexOf("JoinAdditionalServer:m3");
        var w1Idx = events.IndexOf("JoinAgent:w1");
        var w2Idx = events.IndexOf("JoinAgent:w2");

        bootstrapIdx.Should().BeLessThan(m2Idx);
        m2Idx.Should().BeLessThan(m3Idx, because: "additional servers must join one at a time, in order, for etcd quorum safety");
        m3Idx.Should().BeLessThan(w1Idx, because: "agents must only join after the whole control plane is up");
        m3Idx.Should().BeLessThan(w2Idx);

        applier.Registrations.Should().ContainSingle();
        applier.Registrations[0].ServerUrl.Should().Be("https://lb.internal:6443");
    }

    [Fact]
    public async Task Install_ha_topology_joins_masters_sequentially_then_agents_for_k3s()
    {
        var executor = new RecordingNodeExecutor();
        var osDetector = new FakeOsDetector();
        var provisioner = new FakeClusterDistroProvisioner { Kind = DistroKind.K3s, JoinPort = 6443 };
        var monitor = new FakeClusterMonitor();
        var applier = new FakeClusterApplier();
        var processRunner = new RecordingProcessRunner();
        var upgradePlanner = new ClusterUpgradePlanner(executor, osDetector, [provisioner]);
        var service = new ClusterProvisioningService(executor, osDetector, [provisioner], monitor, applier, processRunner, upgradePlanner);

        var topology = new ClusterTopology
        {
            ClusterName = "demo",
            Distro = DistroKind.K3s,
            Version = "v1.30.4+k3s1",
            Nodes = [Server("m1", isInit: true), Server("m2"), Server("m3"), Agent("w1"), Agent("w2")],
            FixedRegistrationAddresses = ["lb.internal"],
            LocalArtifactBundlePath = "./bundle"
        };

        var result = await service.InstallAsync(new ClusterProvisionOptions { Topology = topology });

        result.Ok.Should().BeTrue(because: string.Join("; ", result.Errors));

        var events = provisioner.Events;
        events.Should().Contain("BootstrapFirstServer:m1");
        events.Should().Contain("JoinAdditionalServer:m2");
        events.Should().Contain("JoinAdditionalServer:m3");
        events.Should().Contain("JoinAgent:w1");
        events.Should().Contain("JoinAgent:w2");

        var bootstrapIdx = events.IndexOf("BootstrapFirstServer:m1");
        var m2Idx = events.IndexOf("JoinAdditionalServer:m2");
        var m3Idx = events.IndexOf("JoinAdditionalServer:m3");
        var w1Idx = events.IndexOf("JoinAgent:w1");
        var w2Idx = events.IndexOf("JoinAgent:w2");

        bootstrapIdx.Should().BeLessThan(m2Idx);
        m2Idx.Should().BeLessThan(m3Idx, because: "additional servers must join one at a time, in order, for etcd quorum safety, same as RKE2");
        m3Idx.Should().BeLessThan(w1Idx, because: "agents must only join after the whole control plane is up");
        m3Idx.Should().BeLessThan(w2Idx);

        applier.Registrations.Should().ContainSingle();
        applier.Registrations[0].ServerUrl.Should().Be(
            "https://lb.internal:6443", because: "ApiServerPort is used for the final kubeconfig URL, not JoinPort, even though both are 6443 for k3s");
    }

    [Fact]
    public async Task Install_single_server_topology_does_not_require_fixed_registration_address()
    {
        var (service, _, _, applier, _) = BuildService();
        var topology = new ClusterTopology
        {
            ClusterName = "demo",
            Distro = DistroKind.Rke2,
            Version = "v1",
            Nodes = [Server("m1", isInit: true), Agent("w1")],
            LocalArtifactBundlePath = "./bundle"
        };

        var result = await service.InstallAsync(new ClusterProvisionOptions { Topology = topology });

        result.Ok.Should().BeTrue(because: string.Join("; ", result.Errors));
        applier.Registrations[0].ServerUrl.Should().Be("https://m1:6443");
    }

    [Fact]
    public async Task Install_stops_before_any_join_when_bootstrap_fails()
    {
        var (service, _, provisioner, _, _) = BuildService();
        provisioner.FailBootstrap = true;
        var topology = new ClusterTopology
        {
            ClusterName = "demo",
            Distro = DistroKind.Rke2,
            Version = "v1",
            Nodes = [Server("m1", isInit: true), Server("m2"), Agent("w1")],
            FixedRegistrationAddresses = ["lb.internal"],
            LocalArtifactBundlePath = "./bundle"
        };

        var result = await service.InstallAsync(new ClusterProvisionOptions { Topology = topology });

        result.Ok.Should().BeFalse();
        provisioner.Events.Should().Contain("BootstrapFirstServer:m1");
        provisioner.Events.Should().NotContain(e => e.StartsWith("JoinAdditionalServer", StringComparison.Ordinal));
        provisioner.Events.Should().NotContain(e => e.StartsWith("JoinAgent", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Install_fails_fast_on_invalid_topology_without_touching_nodes()
    {
        var (service, executor, provisioner, _, _) = BuildService();
        var topology = new ClusterTopology
        {
            ClusterName = "demo",
            Distro = DistroKind.Rke2,
            Version = "v1",
            Nodes = [],
            LocalArtifactBundlePath = "./bundle"
        };

        var result = await service.InstallAsync(new ClusterProvisionOptions { Topology = topology });

        result.Ok.Should().BeFalse();
        executor.ExecCalls.Should().BeEmpty();
        provisioner.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Install_dispatches_to_the_provisioner_matching_the_topology_distro()
    {
        var executor = new RecordingNodeExecutor();
        var osDetector = new FakeOsDetector();
        var rke2 = new FakeClusterDistroProvisioner { Kind = DistroKind.Rke2 };
        var k3s = new FakeClusterDistroProvisioner { Kind = DistroKind.K3s, JoinPort = 6443 };
        var monitor = new FakeClusterMonitor();
        var applier = new FakeClusterApplier();
        var processRunner = new RecordingProcessRunner();
        var upgradePlanner = new ClusterUpgradePlanner(executor, osDetector, [rke2, k3s]);
        var service = new ClusterProvisioningService(executor, osDetector, [rke2, k3s], monitor, applier, processRunner, upgradePlanner);

        var topology = new ClusterTopology
        {
            ClusterName = "demo",
            Distro = DistroKind.K3s,
            Version = "v1.30.4+k3s1",
            Nodes = [Server("m1", isInit: true)],
            LocalArtifactBundlePath = "./bundle"
        };

        var result = await service.InstallAsync(new ClusterProvisionOptions { Topology = topology });

        result.Ok.Should().BeTrue(because: string.Join("; ", result.Errors));
        k3s.Events.Should().Contain("BootstrapFirstServer:m1");
        rke2.Events.Should().BeEmpty(because: "the topology asked for k3s, the rke2 provisioner must never be touched");
        applier.Registrations[0].ServerUrl.Should().Be("https://m1:6443");
    }
}
