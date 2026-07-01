using Kubernator.Core.ClusterProvisioning.Discovery;
using Kubernator.Core.ClusterProvisioning.Distros;
using Kubernator.Core.ClusterProvisioning.Ssh;
using Kubernator.Core.Tests.Monitoring;

namespace Kubernator.Core.Tests.ClusterProvisioning.Discovery;

public sealed class ClusterTopologyDiscovererTests
{
    private static ClusterDiscoveryOptions Options(IReadOnlyList<string>? fixedRegistrationAddresses = null) => new()
    {
        ClusterName = "demo",
        Distro = DistroKind.Rke2,
        Version = "v1.30.4+rke2r1",
        LocalArtifactBundlePath = "./bundle",
        SshUsername = "root",
        SshPrivateKeyVaultId = "vault-key-1",
        SshPort = 22,
        FixedRegistrationAddresses = fixedRegistrationAddresses ?? []
    };

    [Fact]
    public async Task Discover_maps_control_plane_label_to_server_role_and_worker_to_agent()
    {
        var runner = new ScriptedProcessRunner();
        runner.Map(["get", "nodes", "-o", "json"], """
            {"items":[
              {
                "metadata":{"name":"m1","labels":{"node-role.kubernetes.io/control-plane":""},"creationTimestamp":"2026-01-01T00:00:00Z"},
                "status":{"addresses":[{"type":"InternalIP","address":"10.0.0.10"}]}
              },
              {
                "metadata":{"name":"w1","labels":{},"creationTimestamp":"2026-01-01T00:05:00Z"},
                "status":{"addresses":[{"type":"InternalIP","address":"10.0.0.20"}]}
              }
            ]}
            """);
        var sut = new ClusterTopologyDiscoverer(runner);

        var result = await sut.DiscoverAsync(Options(["lb.internal"]));

        var m1 = result.Topology.Nodes.Single(n => n.Name == "m1");
        var w1 = result.Topology.Nodes.Single(n => n.Name == "w1");
        m1.Role.Should().Be(NodeRole.Server);
        m1.Connection.Host.Should().Be("10.0.0.10");
        w1.Role.Should().Be(NodeRole.Agent);
        w1.Connection.Host.Should().Be("10.0.0.20");
    }

    [Fact]
    public async Task Discover_picks_oldest_server_by_creationTimestamp_as_init_server()
    {
        var runner = new ScriptedProcessRunner();
        runner.Map(["get", "nodes", "-o", "json"], """
            {"items":[
              {
                "metadata":{"name":"m2","labels":{"node-role.kubernetes.io/control-plane":""},"creationTimestamp":"2026-01-02T00:00:00Z"},
                "status":{"addresses":[{"type":"InternalIP","address":"10.0.0.11"}]}
              },
              {
                "metadata":{"name":"m1","labels":{"node-role.kubernetes.io/control-plane":""},"creationTimestamp":"2026-01-01T00:00:00Z"},
                "status":{"addresses":[{"type":"InternalIP","address":"10.0.0.10"}]}
              }
            ]}
            """);
        var sut = new ClusterTopologyDiscoverer(runner);

        var result = await sut.DiscoverAsync(Options(["lb.internal"]));

        result.Topology.Nodes.Single(n => n.Name == "m1").IsInitServer.Should().BeTrue();
        result.Topology.Nodes.Single(n => n.Name == "m2").IsInitServer.Should().BeFalse();
    }

    [Fact]
    public async Task Discover_falls_back_to_ExternalIP_when_InternalIP_absent()
    {
        var runner = new ScriptedProcessRunner();
        runner.Map(["get", "nodes", "-o", "json"], """
            {"items":[
              {
                "metadata":{"name":"m1","labels":{"node-role.kubernetes.io/master":""},"creationTimestamp":"2026-01-01T00:00:00Z"},
                "status":{"addresses":[{"type":"ExternalIP","address":"203.0.113.5"}]}
              }
            ]}
            """);
        var sut = new ClusterTopologyDiscoverer(runner);

        var result = await sut.DiscoverAsync(Options());

        result.Topology.Nodes.Single().Connection.Host.Should().Be("203.0.113.5");
    }

    [Fact]
    public async Task Discover_applies_shared_ssh_identity_to_every_node()
    {
        var runner = new ScriptedProcessRunner();
        runner.Map(["get", "nodes", "-o", "json"], """
            {"items":[
              {"metadata":{"name":"m1","labels":{"node-role.kubernetes.io/control-plane":""},"creationTimestamp":"2026-01-01T00:00:00Z"},"status":{"addresses":[{"type":"InternalIP","address":"10.0.0.10"}]}},
              {"metadata":{"name":"w1","labels":{},"creationTimestamp":"2026-01-01T00:05:00Z"},"status":{"addresses":[{"type":"InternalIP","address":"10.0.0.20"}]}}
            ]}
            """);
        var sut = new ClusterTopologyDiscoverer(runner);

        var result = await sut.DiscoverAsync(Options(["lb.internal"]) with { SshUsername = "opsuser", SshPort = 2222, SshPrivateKeyVaultId = "vault-1" });

        result.Topology.Nodes.Should().OnlyContain(n =>
            n.Connection.Username == "opsuser" && n.Connection.Port == 2222 && n.Connection.SshPrivateKeyVaultId == "vault-1");
    }

    [Fact]
    public async Task Discover_surfaces_validator_warnings_for_missing_fixed_registration_address_with_multiple_servers()
    {
        var runner = new ScriptedProcessRunner();
        runner.Map(["get", "nodes", "-o", "json"], """
            {"items":[
              {"metadata":{"name":"m1","labels":{"node-role.kubernetes.io/control-plane":""},"creationTimestamp":"2026-01-01T00:00:00Z"},"status":{"addresses":[{"type":"InternalIP","address":"10.0.0.10"}]}},
              {"metadata":{"name":"m2","labels":{"node-role.kubernetes.io/control-plane":""},"creationTimestamp":"2026-01-02T00:00:00Z"},"status":{"addresses":[{"type":"InternalIP","address":"10.0.0.11"}]}}
            ]}
            """);
        var sut = new ClusterTopologyDiscoverer(runner);

        var result = await sut.DiscoverAsync(Options());

        result.Errors.Should().Contain(e => e.Contains("FixedRegistrationAddresses"));
        result.Topology.Nodes.Should().HaveCount(2);
    }
}
