using System.Text;
using Kubernator.Core.ClusterProvisioning.Os;
using Kubernator.Core.ClusterProvisioning.Ssh;

namespace Kubernator.Core.ClusterProvisioning.Distros.K3s;

public sealed class K3sDistroProvisioner : IClusterDistroProvisioner
{
    private const string KubeconfigPath = "/etc/rancher/k3s/k3s.yaml";
    private const string ConfigPath = "/etc/rancher/k3s/config.yaml";
    private static readonly UnixFileMode ExecutableMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
        UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
        UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

    public DistroKind Kind => DistroKind.K3s;

    public int ApiServerPort => 6443;

    public int JoinPort => 6443;

    public async Task<string> ReadKubeconfigAsync(NodeConnection connection, INodeExecutor executor, CancellationToken ct = default)
    {
        var outcome = await executor.ExecuteAsync(
            connection,
            new NodeCommand { CommandLine = $"cat {KubeconfigPath}", UseSudo = true, Timeout = TimeSpan.FromSeconds(15) },
            null, ct);
        if (!outcome.Ok)
        {
            throw new InvalidOperationException($"failed to read k3s kubeconfig: {outcome.StandardError}");
        }
        return outcome.StandardOutput;
    }

    public async Task PrepareOsAsync(
        NodeConnection connection, INodeExecutor executor, OsFacts os, bool permissiveFirewall,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        progress?.Report("preparing OS prerequisites for k3s");

        var script = new StringBuilder();
        script.AppendLine("modprobe overlay || true");
        script.AppendLine("modprobe br_netfilter || true");
        script.AppendLine("cat <<'EOF' > /etc/modules-load.d/k3s.conf");
        script.AppendLine("overlay");
        script.AppendLine("br_netfilter");
        script.AppendLine("EOF");
        script.AppendLine("cat <<'EOF' > /etc/sysctl.d/90-kubernetes.conf");
        script.AppendLine("net.bridge.bridge-nf-call-iptables = 1");
        script.AppendLine("net.bridge.bridge-nf-call-ip6tables = 1");
        script.AppendLine("net.ipv4.ip_forward = 1");
        script.AppendLine("EOF");
        script.AppendLine("sysctl --system >/dev/null 2>&1 || true");
        script.AppendLine("swapoff -a || true");
        script.AppendLine("sed -ri 's/^([^#].*[[:space:]]swap[[:space:]].*)$/#\\1/' /etc/fstab || true");
        script.AppendLine(BuildFirewallStep(os.Firewall, permissiveFirewall));

        var outcome = await executor.ExecuteAsync(
            connection, new NodeCommand { CommandLine = script.ToString(), UseSudo = true, Timeout = TimeSpan.FromMinutes(2) }, progress, ct);
        if (!outcome.Ok)
        {
            throw new InvalidOperationException($"OS preparation failed: {outcome.StandardError}");
        }
    }

    public async Task<string> PrepareArtifactAsync(
        NodeConnection connection, INodeExecutor executor, string localBundlePath, OsFacts os, string version,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var arch = os.Arch;
        var binaryFileName = arch == "arm64" ? "k3s-arm64" : "k3s";
        var binaryPath = Path.Combine(localBundlePath, "artifacts", arch, binaryFileName);
        var imagesPath = Path.Combine(localBundlePath, "artifacts", arch, $"k3s-airgap-images-{arch}.tar.zst");
        var installSh = Path.Combine(localBundlePath, "install.sh");

        if (!File.Exists(binaryPath) || !File.Exists(imagesPath) || !File.Exists(installSh))
        {
            throw new InvalidOperationException($"artifact bundle at {localBundlePath} is missing required files for arch {arch}");
        }

        var remoteDir = $"/opt/kubernator/artifacts/{version}";
        progress?.Report($"staging k3s {version} artifacts ({arch}) on node");

        await executor.UploadFileAsync(connection, binaryPath, "/usr/local/bin/k3s", mode: ExecutableMode, useSudo: true, progress: progress, ct: ct);

        var stageOutcome = await executor.ExecuteAsync(
            connection, new NodeCommand { CommandLine = "mkdir -p /var/lib/rancher/k3s/agent/images", UseSudo = true }, progress, ct);
        if (!stageOutcome.Ok)
        {
            throw new InvalidOperationException($"failed to prepare k3s images directory: {stageOutcome.StandardError}");
        }
        await executor.UploadFileAsync(
            connection, imagesPath, $"/var/lib/rancher/k3s/agent/images/{Path.GetFileName(imagesPath)}", useSudo: true, progress: progress, ct: ct);

        await executor.UploadFileAsync(connection, installSh, $"{remoteDir}/install.sh", mode: ExecutableMode, useSudo: true, progress: progress, ct: ct);

        if (os.Family == OsFamily.RhelLike)
        {
            var selinuxRpm = Path.Combine(localBundlePath, "selinux", arch, "k3s-selinux.rpm");
            if (File.Exists(selinuxRpm))
            {
                var remoteRpm = $"{remoteDir}/k3s-selinux.rpm";
                await executor.UploadFileAsync(connection, selinuxRpm, remoteRpm, useSudo: true, progress: progress, ct: ct);
                progress?.Report("installing k3s-selinux policy");
                await executor.ExecuteAsync(
                    connection, new NodeCommand { CommandLine = $"rpm -i {ShellCommand.Quote(remoteRpm)} || true", UseSudo = true }, progress, ct);
            }
        }

        return remoteDir;
    }

    public Task BootstrapFirstServerAsync(
        NodeConnection connection, INodeExecutor executor, string remoteArtifactDir, ServerBootstrapOptions options,
        IProgress<string>? progress = null, CancellationToken ct = default) =>
        RunServerAsync(connection, executor, remoteArtifactDir, options with { IsFirstServer = true }, progress, ct);

    public Task JoinAdditionalServerAsync(
        NodeConnection connection, INodeExecutor executor, string remoteArtifactDir, ServerBootstrapOptions options,
        IProgress<string>? progress = null, CancellationToken ct = default) =>
        RunServerAsync(connection, executor, remoteArtifactDir, options with { IsFirstServer = false }, progress, ct);

    public async Task JoinAgentAsync(
        NodeConnection connection, INodeExecutor executor, string remoteArtifactDir, AgentJoinOptions options,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var configYaml = K3sConfigTemplate.RenderAgent(options);
        progress?.Report("writing k3s agent config");
        await executor.UploadTextAsync(
            connection, configYaml, ConfigPath,
            mode: UnixFileMode.UserRead | UnixFileMode.UserWrite, useSudo: true, progress: progress, ct: ct);

        var installScript = BuildInstallCommand(remoteArtifactDir, "agent");
        progress?.Report("running k3s install script (agent)");
        var installOutcome = await executor.ExecuteAsync(
            connection, new NodeCommand { CommandLine = installScript, UseSudo = true, Timeout = TimeSpan.FromMinutes(5) }, progress, ct);
        if (!installOutcome.Ok)
        {
            throw new InvalidOperationException($"k3s agent install failed: {installOutcome.StandardError}");
        }

        progress?.Report("enabling k3s-agent service");
        var enableOutcome = await executor.ExecuteAsync(
            connection, new NodeCommand { CommandLine = "systemctl enable --now k3s-agent", UseSudo = true, Timeout = TimeSpan.FromMinutes(3) }, progress, ct);
        if (!enableOutcome.Ok)
        {
            throw new InvalidOperationException($"failed to start k3s-agent: {enableOutcome.StandardError}");
        }
    }

    public async Task<string> ReadJoinTokenAsync(NodeConnection connection, INodeExecutor executor, CancellationToken ct = default)
    {
        var outcome = await executor.ExecuteAsync(
            connection,
            new NodeCommand { CommandLine = "cat /var/lib/rancher/k3s/server/node-token", UseSudo = true, Timeout = TimeSpan.FromSeconds(15) },
            null, ct);
        if (!outcome.Ok)
        {
            throw new InvalidOperationException($"failed to read k3s join token: {outcome.StandardError}");
        }
        var token = outcome.StandardOutput.Trim();
        if (token.Length == 0)
        {
            throw new InvalidOperationException("k3s join token is empty");
        }
        return token;
    }

    public async Task<NodeVersionInfo> GetInstalledVersionAsync(NodeConnection connection, INodeExecutor executor, CancellationToken ct = default)
    {
        const string script = """
            K3S_BIN=$(command -v k3s || echo /usr/local/bin/k3s)
            if [ -x "$K3S_BIN" ]; then
                "$K3S_BIN" --version | head -n1
            else
                echo NOT_INSTALLED
            fi
            if systemctl is-enabled k3s >/dev/null 2>&1; then
                echo ROLE=server
            elif systemctl is-enabled k3s-agent >/dev/null 2>&1; then
                echo ROLE=agent
            else
                echo ROLE=none
            fi
            """;

        var outcome = await executor.ExecuteAsync(
            connection, new NodeCommand { CommandLine = script, Timeout = TimeSpan.FromSeconds(15) }, null, ct);
        if (!outcome.Ok)
        {
            return new NodeVersionInfo { Installed = false };
        }

        var lines = outcome.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var versionLine = lines.FirstOrDefault(l => !l.StartsWith("ROLE=", StringComparison.Ordinal));
        var roleLine = lines.FirstOrDefault(l => l.StartsWith("ROLE=", StringComparison.Ordinal));

        if (versionLine is null || versionLine == "NOT_INSTALLED")
        {
            return new NodeVersionInfo { Installed = false };
        }

        NodeRole? role = roleLine switch
        {
            "ROLE=server" => NodeRole.Server,
            "ROLE=agent" => NodeRole.Agent,
            _ => null
        };

        return new NodeVersionInfo
        {
            Installed = true,
            Version = ExtractVersion(versionLine),
            Role = role
        };
    }

    public async Task UpgradeNodeAsync(
        NodeConnection connection, INodeExecutor executor, string remoteArtifactDir, NodeRole role,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var serviceName = role == NodeRole.Server ? "k3s" : "k3s-agent";
        var installType = role == NodeRole.Server ? "server" : "agent";

        progress?.Report($"stopping {serviceName}");
        await executor.ExecuteAsync(
            connection, new NodeCommand { CommandLine = $"systemctl stop {serviceName}", UseSudo = true, Timeout = TimeSpan.FromMinutes(2) }, progress, ct);

        var installScript = BuildInstallCommand(remoteArtifactDir, installType);
        progress?.Report($"installing new k3s binary ({installType})");
        var installOutcome = await executor.ExecuteAsync(
            connection, new NodeCommand { CommandLine = installScript, UseSudo = true, Timeout = TimeSpan.FromMinutes(5) }, progress, ct);
        if (!installOutcome.Ok)
        {
            throw new InvalidOperationException($"k3s upgrade install failed: {installOutcome.StandardError}");
        }

        progress?.Report($"restarting {serviceName}");
        var startOutcome = await executor.ExecuteAsync(
            connection, new NodeCommand { CommandLine = $"systemctl start {serviceName}", UseSudo = true, Timeout = TimeSpan.FromMinutes(3) }, progress, ct);
        if (!startOutcome.Ok)
        {
            throw new InvalidOperationException($"failed to restart {serviceName}: {startOutcome.StandardError}");
        }

        if (role == NodeRole.Server)
        {
            await WaitForNodeReadyAsync(connection, executor, serviceName, progress, ct);
        }
    }

    private static async Task RunServerAsync(
        NodeConnection connection, INodeExecutor executor, string remoteArtifactDir, ServerBootstrapOptions options,
        IProgress<string>? progress, CancellationToken ct)
    {
        var configYaml = K3sConfigTemplate.RenderServer(options);
        progress?.Report(options.IsFirstServer ? "writing k3s first-server config" : "writing k3s additional-server config");
        await executor.UploadTextAsync(
            connection, configYaml, ConfigPath,
            mode: UnixFileMode.UserRead | UnixFileMode.UserWrite, useSudo: true, progress: progress, ct: ct);

        var installScript = BuildInstallCommand(remoteArtifactDir, "server");
        progress?.Report("running k3s install script (server)");
        var installOutcome = await executor.ExecuteAsync(
            connection, new NodeCommand { CommandLine = installScript, UseSudo = true, Timeout = TimeSpan.FromMinutes(5) }, progress, ct);
        if (!installOutcome.Ok)
        {
            throw new InvalidOperationException($"k3s server install failed: {installOutcome.StandardError}");
        }

        progress?.Report("enabling k3s service");
        var enableOutcome = await executor.ExecuteAsync(
            connection, new NodeCommand { CommandLine = "systemctl enable --now k3s", UseSudo = true, Timeout = TimeSpan.FromMinutes(3) }, progress, ct);
        if (!enableOutcome.Ok)
        {
            throw new InvalidOperationException($"failed to start k3s: {enableOutcome.StandardError}");
        }

        await WaitForNodeReadyAsync(connection, executor, "k3s", progress, ct);
    }

    private static async Task WaitForNodeReadyAsync(
        NodeConnection connection, INodeExecutor executor, string serviceName, IProgress<string>? progress, CancellationToken ct)
    {
        const int maxAttempts = 60;
        const string readinessCheck =
            "export KUBECONFIG=/etc/rancher/k3s/k3s.yaml && " +
            "NODE_NAME=$(hostname) && " +
            "k3s kubectl get node \"$NODE_NAME\" --no-headers 2>/dev/null | awk '{print $2}' | grep -qx Ready && echo READY || echo NOTREADY";

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var check = await executor.ExecuteAsync(
                connection, new NodeCommand { CommandLine = readinessCheck, UseSudo = true, Timeout = TimeSpan.FromSeconds(15) }, null, ct);

            if (check.Ok && check.StandardOutput.Contains("READY", StringComparison.Ordinal))
            {
                progress?.Report($"{serviceName} node is Ready");
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(5), ct);
        }

        throw new InvalidOperationException($"timed out waiting for {serviceName} node to become Ready");
    }

    private static string BuildInstallCommand(string remoteArtifactDir, string installType) =>
        $"INSTALL_K3S_SKIP_DOWNLOAD=true INSTALL_K3S_EXEC={installType} sh {ShellCommand.Quote($"{remoteArtifactDir}/install.sh")}";

    private static string BuildFirewallStep(FirewallKind firewall, bool permissive)
    {
        if (permissive)
        {
            return firewall switch
            {
                FirewallKind.Firewalld => "systemctl disable --now firewalld || true",
                FirewallKind.Ufw => "ufw disable || true",
                _ => ": # no firewall manager detected"
            };
        }

        return firewall switch
        {
            FirewallKind.Firewalld => string.Join(" && ",
                "firewall-cmd --permanent --add-port=6443/tcp",
                "firewall-cmd --permanent --add-port=10250/tcp",
                "firewall-cmd --permanent --add-port=2379-2380/tcp",
                "firewall-cmd --permanent --add-port=8472/udp",
                "firewall-cmd --reload"),
            FirewallKind.Ufw => string.Join(" && ",
                "ufw allow 6443/tcp",
                "ufw allow 10250/tcp",
                "ufw allow 2379:2380/tcp",
                "ufw allow 8472/udp"),
            _ => ": # no firewall manager detected"
        };
    }

    private static string ExtractVersion(string versionOutput)
    {
        var parts = versionOutput.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 3 ? parts[2] : versionOutput;
    }
}
