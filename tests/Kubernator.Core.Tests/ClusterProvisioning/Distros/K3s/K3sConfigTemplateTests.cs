using Kubernator.Core.ClusterProvisioning.Distros;
using Kubernator.Core.ClusterProvisioning.Distros.K3s;

namespace Kubernator.Core.Tests.ClusterProvisioning.Distros.K3s;

public sealed class K3sConfigTemplateTests
{
    [Fact]
    public void RenderServer_first_server_has_cluster_init_and_no_server_line()
    {
        var yaml = K3sConfigTemplate.RenderServer(new ServerBootstrapOptions
        {
            ClusterName = "demo",
            Version = "v1.30.4+k3s1",
            TlsSans = ["10.0.0.10", "cluster.internal"],
            AdvertiseAddress = "10.0.0.10",
            IsFirstServer = true
        });

        yaml.Should().Contain("cluster-init: true");
        yaml.Should().NotContain("server:");
        yaml.Should().Contain("tls-san:");
        yaml.Should().Contain("- 10.0.0.10");
        yaml.Should().Contain("- cluster.internal");
        yaml.Should().NotContain("cni:", because: "k3s has no equivalent config field to RKE2's cni selector");
    }

    [Fact]
    public void RenderServer_additional_server_has_server_and_token_but_no_cluster_init()
    {
        var yaml = K3sConfigTemplate.RenderServer(new ServerBootstrapOptions
        {
            ClusterName = "demo",
            Version = "v1.30.4+k3s1",
            TlsSans = ["10.0.0.10"],
            AdvertiseAddress = "10.0.0.11",
            IsFirstServer = false,
            JoinServerUrl = "https://10.0.0.10:6443",
            Token = "abc123"
        });

        yaml.Should().NotContain("cluster-init");
        yaml.Should().Contain("server: https://10.0.0.10:6443");
        yaml.Should().Contain("token: \"abc123\"");
    }

    [Fact]
    public void RenderServer_additional_server_without_token_throws()
    {
        var act = () => K3sConfigTemplate.RenderServer(new ServerBootstrapOptions
        {
            ClusterName = "demo",
            Version = "v1.30.4+k3s1",
            TlsSans = [],
            AdvertiseAddress = "10.0.0.11",
            IsFirstServer = false,
            JoinServerUrl = "https://10.0.0.10:6443"
        });

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void RenderAgent_has_server_and_token_but_no_tls_san_or_cluster_init()
    {
        var yaml = K3sConfigTemplate.RenderAgent(new AgentJoinOptions
        {
            JoinServerUrl = "https://10.0.0.10:6443",
            Token = "abc123"
        });

        yaml.Should().Contain("server: https://10.0.0.10:6443");
        yaml.Should().Contain("token: \"abc123\"");
        yaml.Should().NotContain("tls-san");
        yaml.Should().NotContain("cluster-init");
    }
}
