using Kubernator.Core.ClusterProvisioning.Distros;
using Kubernator.Core.ClusterProvisioning.Distros.Kubeadm;
using Kubernator.Core.ClusterProvisioning.Ssh;
using Kubernator.Core.Tests.ClusterProvisioning.Fakes;

namespace Kubernator.Core.Tests.ClusterProvisioning.Distros.Kubeadm;

public sealed class KubeadmDistroProvisionerTests
{
    private static readonly NodeConnection Connection = new() { Mode = NodeConnectionMode.Ssh, Host = "m1", Username = "root" };

    private static RecordingNodeExecutor CreateExecutorWithReadyResponder()
    {
        var executor = new RecordingNodeExecutor();
        executor.Responder = call =>
            call.Command.CommandLine.Contains("get node", StringComparison.Ordinal)
                ? new NodeExecOutcome { ExitCode = 0, StandardOutput = "READY", StandardError = "", Duration = TimeSpan.Zero }
                : executor.Default;
        return executor;
    }

    [Fact]
    public async Task BootstrapFirstServerAsync_uploads_init_config_and_applies_flannel_by_default()
    {
        var executor = CreateExecutorWithReadyResponder();
        var sut = new KubeadmDistroProvisioner();

        await sut.BootstrapFirstServerAsync(Connection, executor, "/opt/kubernator/artifacts/v1.30.4", new ServerBootstrapOptions
        {
            ClusterName = "demo",
            Version = "v1.30.4",
            TlsSans = ["10.0.0.10"],
            AdvertiseAddress = "10.0.0.10"
        });

        var configUpload = executor.UploadCalls.Should().ContainSingle(c => c.RemotePath == "/etc/kubernetes/kubeadm-init.yaml").Which;
        configUpload.Content.Should().Contain("kind: InitConfiguration");

        executor.ExecCalls.Should().Contain(c => c.Command.CommandLine.Contains("kubeadm init --config /etc/kubernetes/kubeadm-init.yaml --upload-certs", StringComparison.Ordinal));
        executor.ExecCalls.Should().Contain(c => c.Command.CommandLine.Contains("apply -f", StringComparison.Ordinal) && c.Command.CommandLine.Contains("flannel.yaml", StringComparison.Ordinal));
        executor.ExecCalls.Should().Contain(c => c.Command.CommandLine.Contains("get node", StringComparison.Ordinal));
    }

    [Fact]
    public async Task BootstrapFirstServerAsync_applies_calico_when_requested()
    {
        var executor = CreateExecutorWithReadyResponder();
        var sut = new KubeadmDistroProvisioner();

        await sut.BootstrapFirstServerAsync(Connection, executor, "/opt/kubernator/artifacts/v1.30.4", new ServerBootstrapOptions
        {
            ClusterName = "demo",
            Version = "v1.30.4",
            TlsSans = ["10.0.0.10"],
            AdvertiseAddress = "10.0.0.10",
            CniPlugin = "calico"
        });

        executor.ExecCalls.Should().Contain(c => c.Command.CommandLine.Contains("apply -f", StringComparison.Ordinal) && c.Command.CommandLine.Contains("calico.yaml", StringComparison.Ordinal));
    }

    [Fact]
    public async Task JoinAdditionalServerAsync_uploads_join_config_with_control_plane_block()
    {
        var executor = CreateExecutorWithReadyResponder();
        var sut = new KubeadmDistroProvisioner();
        var token = KubeadmJoinToken.Encode("abcdef.0123456789abcdef", "deadbeef", "cafef00d");

        await sut.JoinAdditionalServerAsync(Connection, executor, "/opt/kubernator/artifacts/v1.30.4", new ServerBootstrapOptions
        {
            ClusterName = "demo",
            Version = "v1.30.4",
            TlsSans = ["10.0.0.10"],
            AdvertiseAddress = "10.0.0.11",
            JoinServerUrl = "https://10.0.0.10:6443",
            Token = token
        });

        var configUpload = executor.UploadCalls.Should().ContainSingle(c => c.RemotePath == "/etc/kubernetes/kubeadm-join.yaml").Which;
        configUpload.Content.Should().Contain("controlPlane:");
        configUpload.Content.Should().Contain("certificateKey");

        executor.ExecCalls.Should().Contain(c => c.Command.CommandLine == "kubeadm join --config /etc/kubernetes/kubeadm-join.yaml");
        executor.ExecCalls.Should().Contain(c => c.Command.CommandLine.Contains("get node", StringComparison.Ordinal));
    }

    [Fact]
    public async Task JoinAgentAsync_uploads_join_config_without_control_plane_block_and_does_not_wait_for_ready()
    {
        var executor = new RecordingNodeExecutor();
        var sut = new KubeadmDistroProvisioner();
        var token = KubeadmJoinToken.Encode("abcdef.0123456789abcdef", "deadbeef", null);

        await sut.JoinAgentAsync(Connection, executor, "/opt/kubernator/artifacts/v1.30.4", new AgentJoinOptions
        {
            JoinServerUrl = "https://10.0.0.10:6443",
            Token = token
        });

        var configUpload = executor.UploadCalls.Should().ContainSingle(c => c.RemotePath == "/etc/kubernetes/kubeadm-join.yaml").Which;
        configUpload.Content.Should().NotContain("controlPlane:");

        executor.ExecCalls.Should().Contain(c => c.Command.CommandLine == "kubeadm join --config /etc/kubernetes/kubeadm-join.yaml");
        executor.ExecCalls.Should().NotContain(c => c.Command.CommandLine.Contains("get node", StringComparison.Ordinal),
            because: "agents are not waited on for readiness");
    }

    [Fact]
    public async Task ReadJoinTokenAsync_combines_bootstrap_token_ca_hash_and_certificate_key()
    {
        var executor = new RecordingNodeExecutor
        {
            Responder = call =>
            {
                var cmd = call.Command.CommandLine;
                if (cmd.Contains("token create --print-join-command", StringComparison.Ordinal))
                {
                    return new NodeExecOutcome
                    {
                        ExitCode = 0,
                        StandardOutput = "kubeadm join 10.0.0.10:6443 --token abcdef.0123456789abcdef --discovery-token-ca-cert-hash sha256:deadbeef",
                        StandardError = "",
                        Duration = TimeSpan.Zero
                    };
                }
                if (cmd.Contains("upload-certs --upload-certs", StringComparison.Ordinal))
                {
                    return new NodeExecOutcome
                    {
                        ExitCode = 0,
                        StandardOutput = "[upload-certs] Storing the certificates in Secret\n" + new string('a', 64),
                        StandardError = "",
                        Duration = TimeSpan.Zero
                    };
                }
                return new NodeExecOutcome { ExitCode = 1, StandardOutput = "", StandardError = "unexpected command", Duration = TimeSpan.Zero };
            }
        };
        var sut = new KubeadmDistroProvisioner();

        var token = await sut.ReadJoinTokenAsync(Connection, executor);

        var (bootstrapToken, caCertHash, certificateKey) = KubeadmJoinToken.Decode(token);
        bootstrapToken.Should().Be("abcdef.0123456789abcdef");
        caCertHash.Should().Be("deadbeef");
        certificateKey.Should().Be(new string('a', 64));
    }

    [Fact]
    public async Task GetInstalledVersionAsync_parses_version_and_role_from_script_output()
    {
        var executor = new RecordingNodeExecutor
        {
            Default = new NodeExecOutcome
            {
                ExitCode = 0,
                StandardOutput = """{"clientVersion":{"gitVersion":"v1.30.4"}}""" + "\nROLE=server",
                StandardError = "",
                Duration = TimeSpan.Zero
            }
        };
        var sut = new KubeadmDistroProvisioner();

        var info = await sut.GetInstalledVersionAsync(Connection, executor);

        info.Installed.Should().BeTrue();
        info.Version.Should().Be("v1.30.4");
        info.Role.Should().Be(NodeRole.Server);
    }

    [Fact]
    public async Task UpgradeNodeAsync_for_agent_role_stages_binaries_and_restarts_kubelet_without_kubeadm_upgrade()
    {
        var executor = new RecordingNodeExecutor();
        var sut = new KubeadmDistroProvisioner();

        await sut.UpgradeNodeAsync(Connection, executor, "/opt/kubernator/artifacts/v1.30.5", NodeRole.Agent);

        executor.ExecCalls.Should().Contain(c => c.Command.CommandLine.Contains("/usr/local/bin/kubeadm", StringComparison.Ordinal));
        executor.ExecCalls.Should().Contain(c => c.Command.CommandLine == "systemctl restart kubelet");
        executor.ExecCalls.Should().NotContain(c => c.Command.CommandLine.Contains("kubeadm upgrade", StringComparison.Ordinal));
        executor.ExecCalls.Should().NotContain(c => c.Command.CommandLine.Contains("get node", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UpgradeNodeAsync_for_server_role_runs_upgrade_apply_when_no_node_is_already_on_target_version()
    {
        var executor = new RecordingNodeExecutor
        {
            Responder = call =>
                call.Command.CommandLine.Contains("kubeletVersion", StringComparison.Ordinal)
                    ? new NodeExecOutcome { ExitCode = 0, StandardOutput = "v1.30.4", StandardError = "", Duration = TimeSpan.Zero }
                    : new NodeExecOutcome { ExitCode = 0, StandardOutput = "READY", StandardError = "", Duration = TimeSpan.Zero }
        };
        var sut = new KubeadmDistroProvisioner();

        await sut.UpgradeNodeAsync(Connection, executor, "/opt/kubernator/artifacts/v1.30.5", NodeRole.Server);

        executor.ExecCalls.Should().Contain(c => c.Command.CommandLine.Contains("kubeadm upgrade apply", StringComparison.Ordinal) && c.Command.CommandLine.Contains("v1.30.5", StringComparison.Ordinal));
        executor.ExecCalls.Should().NotContain(c => c.Command.CommandLine == "kubeadm upgrade node");
    }

    [Fact]
    public async Task UpgradeNodeAsync_for_server_role_runs_upgrade_node_when_another_node_is_already_on_target_version()
    {
        var executor = new RecordingNodeExecutor
        {
            Responder = call =>
                call.Command.CommandLine.Contains("kubeletVersion", StringComparison.Ordinal)
                    ? new NodeExecOutcome { ExitCode = 0, StandardOutput = "v1.30.4 v1.30.5", StandardError = "", Duration = TimeSpan.Zero }
                    : new NodeExecOutcome { ExitCode = 0, StandardOutput = "READY", StandardError = "", Duration = TimeSpan.Zero }
        };
        var sut = new KubeadmDistroProvisioner();

        await sut.UpgradeNodeAsync(Connection, executor, "/opt/kubernator/artifacts/v1.30.5", NodeRole.Server);

        executor.ExecCalls.Should().Contain(c => c.Command.CommandLine == "kubeadm upgrade node");
        executor.ExecCalls.Should().NotContain(c => c.Command.CommandLine.Contains("upgrade apply", StringComparison.Ordinal));
    }
}
