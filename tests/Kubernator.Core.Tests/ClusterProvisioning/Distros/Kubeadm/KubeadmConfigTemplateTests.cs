using Kubernator.Core.ClusterProvisioning.Distros;
using Kubernator.Core.ClusterProvisioning.Distros.Kubeadm;

namespace Kubernator.Core.Tests.ClusterProvisioning.Distros.Kubeadm;

public sealed class KubeadmConfigTemplateTests
{
    [Fact]
    public void RenderInit_has_init_and_cluster_configuration_with_no_discovery_block()
    {
        var yaml = KubeadmConfigTemplate.RenderInit(new ServerBootstrapOptions
        {
            ClusterName = "demo",
            Version = "v1.30.4",
            TlsSans = ["10.0.0.10", "cluster.internal"],
            AdvertiseAddress = "10.0.0.10",
            IsFirstServer = true
        });

        yaml.Should().Contain("kind: InitConfiguration");
        yaml.Should().Contain("kind: ClusterConfiguration");
        yaml.Should().Contain("kubernetesVersion: v1.30.4");
        yaml.Should().Contain("controlPlaneEndpoint: 10.0.0.10:6443");
        yaml.Should().Contain("- 10.0.0.10");
        yaml.Should().Contain("- cluster.internal");
        yaml.Should().NotContain("discovery:");
        yaml.Should().NotContain("kind: JoinConfiguration");
    }

    [Fact]
    public void RenderJoinControlPlane_has_discovery_and_certificate_key()
    {
        var token = KubeadmJoinToken.Encode("abcdef.0123456789abcdef", "deadbeef", "cafef00d");

        var yaml = KubeadmConfigTemplate.RenderJoinControlPlane(new ServerBootstrapOptions
        {
            ClusterName = "demo",
            Version = "v1.30.4",
            TlsSans = ["10.0.0.10"],
            AdvertiseAddress = "10.0.0.11",
            IsFirstServer = false,
            JoinServerUrl = "https://10.0.0.10:6443",
            Token = token
        });

        yaml.Should().Contain("kind: JoinConfiguration");
        yaml.Should().Contain("discovery:");
        yaml.Should().Contain("token: \"abcdef.0123456789abcdef\"");
        yaml.Should().Contain("apiServerEndpoint: 10.0.0.10:6443");
        yaml.Should().Contain("caCertHashes: [\"deadbeef\"]");
        yaml.Should().Contain("controlPlane:");
        yaml.Should().Contain("certificateKey: \"cafef00d\"");
    }

    [Fact]
    public void RenderJoinControlPlane_without_certificate_key_throws()
    {
        var token = KubeadmJoinToken.Encode("abcdef.0123456789abcdef", "deadbeef", null);

        var act = () => KubeadmConfigTemplate.RenderJoinControlPlane(new ServerBootstrapOptions
        {
            ClusterName = "demo",
            Version = "v1.30.4",
            TlsSans = [],
            AdvertiseAddress = "10.0.0.11",
            IsFirstServer = false,
            JoinServerUrl = "https://10.0.0.10:6443",
            Token = token
        });

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void RenderJoinControlPlane_without_token_throws()
    {
        var act = () => KubeadmConfigTemplate.RenderJoinControlPlane(new ServerBootstrapOptions
        {
            ClusterName = "demo",
            Version = "v1.30.4",
            TlsSans = [],
            AdvertiseAddress = "10.0.0.11",
            IsFirstServer = false,
            JoinServerUrl = "https://10.0.0.10:6443"
        });

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void RenderJoinWorker_has_discovery_but_no_control_plane_block()
    {
        var token = KubeadmJoinToken.Encode("abcdef.0123456789abcdef", "deadbeef", null);

        var yaml = KubeadmConfigTemplate.RenderJoinWorker(new AgentJoinOptions
        {
            JoinServerUrl = "https://10.0.0.10:6443",
            Token = token
        });

        yaml.Should().Contain("kind: JoinConfiguration");
        yaml.Should().Contain("discovery:");
        yaml.Should().Contain("apiServerEndpoint: 10.0.0.10:6443");
        yaml.Should().NotContain("controlPlane:");
        yaml.Should().NotContain("certificateKey");
    }
}
