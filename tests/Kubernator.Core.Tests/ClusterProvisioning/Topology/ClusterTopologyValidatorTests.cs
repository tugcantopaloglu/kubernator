using Kubernator.Core.ClusterProvisioning.Distros;
using Kubernator.Core.ClusterProvisioning.Ssh;
using Kubernator.Core.ClusterProvisioning.Topology;

namespace Kubernator.Core.Tests.ClusterProvisioning.Topology;

public sealed class ClusterTopologyValidatorTests
{
    private static NodeSpec Server(string name, bool isInit = false) => new()
    {
        Name = name,
        Role = NodeRole.Server,
        Connection = new NodeConnection { Mode = NodeConnectionMode.Ssh, Host = $"{name}.internal" },
        AdvertiseAddress = $"{name}.internal",
        IsInitServer = isInit
    };

    private static NodeSpec Agent(string name) => new()
    {
        Name = name,
        Role = NodeRole.Agent,
        Connection = new NodeConnection { Mode = NodeConnectionMode.Ssh, Host = $"{name}.internal" }
    };

    [Fact]
    public void Valid_ha_topology_passes_with_no_errors()
    {
        var topology = new ClusterTopology
        {
            ClusterName = "demo",
            Distro = DistroKind.Rke2,
            Version = "v1.30.4+rke2r1",
            Nodes = [Server("m1", isInit: true), Server("m2"), Server("m3"), Agent("w1")],
            FixedRegistrationAddresses = ["lb.internal"],
            LocalArtifactBundlePath = "./bundle"
        };

        var result = ClusterTopologyValidator.Validate(topology);

        result.Ok.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Empty_topology_is_an_error()
    {
        var topology = new ClusterTopology
        {
            ClusterName = "demo",
            Distro = DistroKind.Rke2,
            Version = "v1",
            Nodes = [],
            LocalArtifactBundlePath = "./bundle"
        };

        var result = ClusterTopologyValidator.Validate(topology);

        result.Ok.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Contains("no nodes"));
    }

    [Fact]
    public void Zero_init_servers_is_an_error()
    {
        var topology = new ClusterTopology
        {
            ClusterName = "demo",
            Distro = DistroKind.Rke2,
            Version = "v1",
            Nodes = [Server("m1"), Agent("w1")],
            LocalArtifactBundlePath = "./bundle"
        };

        var result = ClusterTopologyValidator.Validate(topology);

        result.Errors.Should().Contain(e => e.Contains("exactly one server node must have IsInitServer"));
    }

    [Fact]
    public void Multiple_init_servers_is_an_error()
    {
        var topology = new ClusterTopology
        {
            ClusterName = "demo",
            Distro = DistroKind.Rke2,
            Version = "v1",
            Nodes = [Server("m1", isInit: true), Server("m2", isInit: true)],
            FixedRegistrationAddresses = ["lb.internal"],
            LocalArtifactBundlePath = "./bundle"
        };

        var result = ClusterTopologyValidator.Validate(topology);

        result.Errors.Should().Contain(e => e.Contains("only one server node may have IsInitServer"));
    }

    [Fact]
    public void Duplicate_node_names_is_an_error()
    {
        var topology = new ClusterTopology
        {
            ClusterName = "demo",
            Distro = DistroKind.Rke2,
            Version = "v1",
            Nodes = [Server("m1", isInit: true), Agent("m1")],
            LocalArtifactBundlePath = "./bundle"
        };

        var result = ClusterTopologyValidator.Validate(topology);

        result.Errors.Should().Contain(e => e.Contains("duplicate node name"));
    }

    [Fact]
    public void Multiple_servers_without_fixed_registration_address_is_an_error()
    {
        var topology = new ClusterTopology
        {
            ClusterName = "demo",
            Distro = DistroKind.Rke2,
            Version = "v1",
            Nodes = [Server("m1", isInit: true), Server("m2"), Server("m3")],
            LocalArtifactBundlePath = "./bundle"
        };

        var result = ClusterTopologyValidator.Validate(topology);

        result.Errors.Should().Contain(e => e.Contains("FixedRegistrationAddresses is required"));
    }

    [Fact]
    public void Even_server_count_is_a_warning_not_an_error()
    {
        var topology = new ClusterTopology
        {
            ClusterName = "demo",
            Distro = DistroKind.Rke2,
            Version = "v1",
            Nodes = [Server("m1", isInit: true), Server("m2")],
            FixedRegistrationAddresses = ["lb.internal"],
            LocalArtifactBundlePath = "./bundle"
        };

        var result = ClusterTopologyValidator.Validate(topology);

        result.Ok.Should().BeTrue();
        result.Warnings.Should().Contain(w => w.Contains("even"));
    }

    [Fact]
    public void No_agents_is_a_warning_not_an_error()
    {
        var topology = new ClusterTopology
        {
            ClusterName = "demo",
            Distro = DistroKind.Rke2,
            Version = "v1",
            Nodes = [Server("m1", isInit: true)],
            LocalArtifactBundlePath = "./bundle"
        };

        var result = ClusterTopologyValidator.Validate(topology);

        result.Ok.Should().BeTrue();
        result.Warnings.Should().Contain(w => w.Contains("no agent"));
    }

    [Fact]
    public void Kubeadm_topology_with_default_canal_cni_is_invalid()
    {
        var topology = new ClusterTopology
        {
            ClusterName = "demo",
            Distro = DistroKind.KubeadmNative,
            Version = "v1.30.4",
            Nodes = [Server("m1", isInit: true), Agent("w1")],
            LocalArtifactBundlePath = "./bundle"
        };

        var result = ClusterTopologyValidator.Validate(topology);

        result.Errors.Should().Contain(e => e.Contains("cniPlugin") && e.Contains("flannel"));
    }

    [Theory]
    [InlineData("flannel")]
    [InlineData("calico")]
    public void Kubeadm_topology_with_flannel_or_calico_cni_is_valid(string cniPlugin)
    {
        var topology = new ClusterTopology
        {
            ClusterName = "demo",
            Distro = DistroKind.KubeadmNative,
            Version = "v1.30.4",
            CniPlugin = cniPlugin,
            Nodes = [Server("m1", isInit: true), Agent("w1")],
            LocalArtifactBundlePath = "./bundle"
        };

        var result = ClusterTopologyValidator.Validate(topology);

        result.Ok.Should().BeTrue();
    }

    [Fact]
    public void Rke2_topology_is_unaffected_by_the_kubeadm_cni_rule()
    {
        var topology = new ClusterTopology
        {
            ClusterName = "demo",
            Distro = DistroKind.Rke2,
            Version = "v1.30.4+rke2r1",
            Nodes = [Server("m1", isInit: true), Agent("w1")],
            LocalArtifactBundlePath = "./bundle"
        };

        var result = ClusterTopologyValidator.Validate(topology);

        result.Ok.Should().BeTrue();
    }

    [Fact]
    public void Ssh_node_without_host_is_an_error()
    {
        var topology = new ClusterTopology
        {
            ClusterName = "demo",
            Distro = DistroKind.Rke2,
            Version = "v1",
            Nodes = [new NodeSpec { Name = "m1", Role = NodeRole.Server, IsInitServer = true, Connection = new NodeConnection { Mode = NodeConnectionMode.Ssh } }],
            LocalArtifactBundlePath = "./bundle"
        };

        var result = ClusterTopologyValidator.Validate(topology);

        result.Errors.Should().Contain(e => e.Contains("no Host configured"));
    }
}
