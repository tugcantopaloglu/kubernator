using Kubernator.Core.ClusterProvisioning.Distros;
using Kubernator.Core.ClusterProvisioning.Distros.K3s;
using Kubernator.Core.ClusterProvisioning.Ssh;
using Kubernator.Core.Tests.ClusterProvisioning.Fakes;

namespace Kubernator.Core.Tests.ClusterProvisioning.Distros.K3s;

public sealed class K3sDistroProvisionerTests
{
    private static readonly NodeConnection Connection = new() { Mode = NodeConnectionMode.Ssh, Host = "m1", Username = "root" };

    private static RecordingNodeExecutor CreateExecutorWithReadyResponder()
    {
        var executor = new RecordingNodeExecutor();
        executor.Responder = call =>
            call.Command.CommandLine.Contains("kubectl get node", StringComparison.Ordinal)
                ? new NodeExecOutcome { ExitCode = 0, StandardOutput = "READY", StandardError = "", Duration = TimeSpan.Zero }
                : executor.Default;
        return executor;
    }

    [Fact]
    public async Task BootstrapFirstServerAsync_uploads_config_with_cluster_init_and_enables_service()
    {
        var executor = CreateExecutorWithReadyResponder();
        var sut = new K3sDistroProvisioner();

        await sut.BootstrapFirstServerAsync(Connection, executor, "/opt/kubernator/artifacts/v1.30.4+k3s1", new ServerBootstrapOptions
        {
            ClusterName = "demo",
            Version = "v1.30.4+k3s1",
            TlsSans = ["10.0.0.10"],
            AdvertiseAddress = "10.0.0.10",
            Token = "tok"
        });

        var configUpload = executor.UploadCalls.Should().ContainSingle(c => c.RemotePath == "/etc/rancher/k3s/config.yaml").Which;
        configUpload.UseSudo.Should().BeTrue();
        configUpload.Content.Should().Contain("cluster-init: true");
        configUpload.Content.Should().Contain("tls-san:");
        configUpload.Content.Should().NotContain("cni:", because: "k3s has no equivalent config field to RKE2's cni selector");

        executor.ExecCalls.Should().Contain(c => c.Command.CommandLine.Contains("INSTALL_K3S_EXEC=server", StringComparison.Ordinal));
        executor.ExecCalls.Should().Contain(c => c.Command.CommandLine == "systemctl enable --now k3s");
        executor.ExecCalls.Should().Contain(c => c.Command.CommandLine.Contains("kubectl get node", StringComparison.Ordinal));
    }

    [Fact]
    public async Task JoinAdditionalServerAsync_uploads_config_with_server_and_token_but_no_cluster_init()
    {
        var executor = CreateExecutorWithReadyResponder();
        var sut = new K3sDistroProvisioner();

        await sut.JoinAdditionalServerAsync(Connection, executor, "/opt/kubernator/artifacts/v1.30.4+k3s1", new ServerBootstrapOptions
        {
            ClusterName = "demo",
            Version = "v1.30.4+k3s1",
            TlsSans = [],
            AdvertiseAddress = "10.0.0.11",
            JoinServerUrl = "https://10.0.0.10:6443",
            Token = "tok"
        });

        var configUpload = executor.UploadCalls.Should().ContainSingle(c => c.RemotePath == "/etc/rancher/k3s/config.yaml").Which;
        configUpload.Content.Should().Contain("server:");
        configUpload.Content.Should().Contain("token:");
        configUpload.Content.Should().NotContain("cluster-init");
    }

    [Fact]
    public async Task JoinAgentAsync_uploads_agent_config_and_enables_k3s_agent_service()
    {
        var executor = new RecordingNodeExecutor();
        var sut = new K3sDistroProvisioner();

        await sut.JoinAgentAsync(Connection, executor, "/opt/kubernator/artifacts/v1.30.4+k3s1", new AgentJoinOptions
        {
            JoinServerUrl = "https://10.0.0.10:6443",
            Token = "tok"
        });

        var configUpload = executor.UploadCalls.Should().ContainSingle(c => c.RemotePath == "/etc/rancher/k3s/config.yaml").Which;
        configUpload.Content.Should().Contain("server:");
        configUpload.Content.Should().Contain("token:");
        configUpload.Content.Should().NotContain("tls-san");
        configUpload.Content.Should().NotContain("cluster-init");

        executor.ExecCalls.Should().Contain(c => c.Command.CommandLine.Contains("INSTALL_K3S_EXEC=agent", StringComparison.Ordinal));
        executor.ExecCalls.Should().Contain(c => c.Command.CommandLine == "systemctl enable --now k3s-agent");
        executor.ExecCalls.Should().NotContain(c => c.Command.CommandLine.Contains("kubectl get node", StringComparison.Ordinal),
            because: "agents are not waited on for readiness");
    }

    [Fact]
    public async Task GetInstalledVersionAsync_parses_role_and_version_from_script_output()
    {
        var executor = new RecordingNodeExecutor
        {
            Default = new NodeExecOutcome
            {
                ExitCode = 0,
                StandardOutput = "k3s version v1.30.4+k3s1 (abcdef)\nROLE=server",
                StandardError = "",
                Duration = TimeSpan.Zero
            }
        };
        var sut = new K3sDistroProvisioner();

        var info = await sut.GetInstalledVersionAsync(Connection, executor);

        info.Installed.Should().BeTrue();
        info.Version.Should().Be("v1.30.4+k3s1");
        info.Role.Should().Be(NodeRole.Server);
    }

    [Fact]
    public async Task UpgradeNodeAsync_for_agent_role_stops_installs_and_restarts_without_waiting_for_ready()
    {
        var executor = new RecordingNodeExecutor();
        var sut = new K3sDistroProvisioner();

        await sut.UpgradeNodeAsync(Connection, executor, "/opt/kubernator/artifacts/v1.30.5+k3s1", NodeRole.Agent);

        executor.ExecCalls.Should().Contain(c => c.Command.CommandLine == "systemctl stop k3s-agent");
        executor.ExecCalls.Should().Contain(c => c.Command.CommandLine.Contains("INSTALL_K3S_EXEC=agent", StringComparison.Ordinal));
        executor.ExecCalls.Should().Contain(c => c.Command.CommandLine == "systemctl start k3s-agent");
        executor.ExecCalls.Should().NotContain(c => c.Command.CommandLine.Contains("kubectl get node", StringComparison.Ordinal),
            because: "only server upgrades wait for readiness");
    }
}
