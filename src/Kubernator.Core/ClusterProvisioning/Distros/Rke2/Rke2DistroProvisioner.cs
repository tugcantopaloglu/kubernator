using System.Text;
using Kubernator.Core.ClusterProvisioning.Os;
using Kubernator.Core.ClusterProvisioning.Ssh;

namespace Kubernator.Core.ClusterProvisioning.Distros.Rke2;

public sealed class Rke2DistroProvisioner : IClusterDistroProvisioner
{
    private const string KubeconfigPath = "/etc/rancher/rke2/rke2.yaml";

    public DistroKind Kind => DistroKind.Rke2;

    public int ApiServerPort => 6443;

    public int JoinPort => 9345;

    public async Task<string> ReadKubeconfigAsync(NodeConnection connection, INodeExecutor executor, CancellationToken ct = default)
    {
        var outcome = await executor.ExecuteAsync(
            connection,
            new NodeCommand { CommandLine = $"cat {KubeconfigPath}", UseSudo = true, Timeout = TimeSpan.FromSeconds(15) },
            null, ct);
        if (!outcome.Ok)
        {
            throw new InvalidOperationException($"failed to read RKE2 kubeconfig: {outcome.StandardError}");
        }
        return outcome.StandardOutput;
    }

    public async Task PrepareOsAsync(
        NodeConnection connection, INodeExecutor executor, OsFacts os, bool permissiveFirewall,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        progress?.Report("preparing OS prerequisites for RKE2");

        var script = new StringBuilder();
        script.AppendLine("modprobe overlay || true");
        script.AppendLine("modprobe br_netfilter || true");
        script.AppendLine("cat <<'EOF' > /etc/modules-load.d/rke2.conf");
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
            connection,
            new NodeCommand { CommandLine = script.ToString(), UseSudo = true, Timeout = TimeSpan.FromMinutes(2) },
            progress,
            ct);
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
        var artifactTar = Path.Combine(localBundlePath, "artifacts", arch, $"rke2.linux-{arch}.tar.gz");
        var imagesTar = Path.Combine(localBundlePath, "artifacts", arch, $"rke2-images.linux-{arch}.tar.zst");
        var installSh = Path.Combine(localBundlePath, "install.sh");
        var selinuxRpm = Path.Combine(localBundlePath, "selinux", arch, "rke2-selinux.rpm");

        if (!File.Exists(artifactTar) || !File.Exists(imagesTar) || !File.Exists(installSh))
        {
            throw new InvalidOperationException($"artifact bundle at {localBundlePath} is missing required files for arch {arch}");
        }

        var remoteDir = $"/opt/kubernator/artifacts/{version}";
        progress?.Report($"staging RKE2 {version} artifacts ({arch}) on node");

        await executor.UploadFileAsync(connection, artifactTar, $"{remoteDir}/{Path.GetFileName(artifactTar)}", useSudo: true, progress: progress, ct: ct);
        await executor.UploadFileAsync(connection, imagesTar, $"{remoteDir}/{Path.GetFileName(imagesTar)}", useSudo: true, progress: progress, ct: ct);
        await executor.UploadFileAsync(
            connection, installSh, $"{remoteDir}/install.sh",
            mode: UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                  UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                  UnixFileMode.OtherRead | UnixFileMode.OtherExecute,
            useSudo: true, progress: progress, ct: ct);

        var stageImagesScript =
            $"mkdir -p /var/lib/rancher/rke2/agent/images && cp {ShellCommand.Quote($"{remoteDir}/{Path.GetFileName(imagesTar)}")} /var/lib/rancher/rke2/agent/images/";
        var stageOutcome = await executor.ExecuteAsync(
            connection, new NodeCommand { CommandLine = stageImagesScript, UseSudo = true }, progress, ct);
        if (!stageOutcome.Ok)
        {
            throw new InvalidOperationException($"failed to stage RKE2 images: {stageOutcome.StandardError}");
        }

        if (os.Family == OsFamily.RhelLike && File.Exists(selinuxRpm))
        {
            var remoteRpm = $"{remoteDir}/rke2-selinux.rpm";
            await executor.UploadFileAsync(connection, selinuxRpm, remoteRpm, useSudo: true, progress: progress, ct: ct);
            progress?.Report("installing rke2-selinux policy");
            await executor.ExecuteAsync(
                connection,
                new NodeCommand { CommandLine = $"rpm -i {ShellCommand.Quote(remoteRpm)} || true", UseSudo = true },
                progress, ct);
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
        var configYaml = Rke2ConfigTemplate.RenderAgent(options);
        progress?.Report("writing RKE2 agent config");
        await executor.UploadTextAsync(
            connection, configYaml, "/etc/rancher/rke2/config.yaml",
            mode: UnixFileMode.UserRead | UnixFileMode.UserWrite, useSudo: true, progress: progress, ct: ct);

        var installScript = BuildInstallCommand(remoteArtifactDir, "agent");
        progress?.Report("running RKE2 install script (agent)");
        var installOutcome = await executor.ExecuteAsync(
            connection, new NodeCommand { CommandLine = installScript, UseSudo = true, Timeout = TimeSpan.FromMinutes(5) }, progress, ct);
        if (!installOutcome.Ok)
        {
            throw new InvalidOperationException($"RKE2 agent install failed: {installOutcome.StandardError}");
        }

        progress?.Report("enabling rke2-agent service");
        var enableOutcome = await executor.ExecuteAsync(
            connection, new NodeCommand { CommandLine = "systemctl enable --now rke2-agent", UseSudo = true, Timeout = TimeSpan.FromMinutes(3) }, progress, ct);
        if (!enableOutcome.Ok)
        {
            throw new InvalidOperationException($"failed to start rke2-agent: {enableOutcome.StandardError}");
        }
    }

    public async Task<string> ReadJoinTokenAsync(NodeConnection connection, INodeExecutor executor, CancellationToken ct = default)
    {
        var outcome = await executor.ExecuteAsync(
            connection,
            new NodeCommand { CommandLine = "cat /var/lib/rancher/rke2/server/node-token", UseSudo = true, Timeout = TimeSpan.FromSeconds(15) },
            null, ct);
        if (!outcome.Ok)
        {
            throw new InvalidOperationException($"failed to read RKE2 join token: {outcome.StandardError}");
        }
        var token = outcome.StandardOutput.Trim();
        if (token.Length == 0)
        {
            throw new InvalidOperationException("RKE2 join token is empty");
        }
        return token;
    }

    public async Task<NodeVersionInfo> GetInstalledVersionAsync(NodeConnection connection, INodeExecutor executor, CancellationToken ct = default)
    {
        const string script = """
            RKE2_BIN=$(command -v rke2 || echo /usr/local/bin/rke2)
            if [ -x "$RKE2_BIN" ]; then
                "$RKE2_BIN" --version | head -n1
            else
                echo NOT_INSTALLED
            fi
            if systemctl is-enabled rke2-server >/dev/null 2>&1; then
                echo ROLE=server
            elif systemctl is-enabled rke2-agent >/dev/null 2>&1; then
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
        var serviceName = role == NodeRole.Server ? "rke2-server" : "rke2-agent";
        var installType = role == NodeRole.Server ? "server" : "agent";

        progress?.Report($"stopping {serviceName}");
        await executor.ExecuteAsync(
            connection, new NodeCommand { CommandLine = $"systemctl stop {serviceName}", UseSudo = true, Timeout = TimeSpan.FromMinutes(2) }, progress, ct);

        var installScript = BuildInstallCommand(remoteArtifactDir, installType);
        progress?.Report($"installing new RKE2 binaries ({installType})");
        var installOutcome = await executor.ExecuteAsync(
            connection, new NodeCommand { CommandLine = installScript, UseSudo = true, Timeout = TimeSpan.FromMinutes(5) }, progress, ct);
        if (!installOutcome.Ok)
        {
            throw new InvalidOperationException($"RKE2 upgrade install failed: {installOutcome.StandardError}");
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
        var configYaml = Rke2ConfigTemplate.RenderServer(options);
        progress?.Report(options.IsFirstServer ? "writing RKE2 first-server config" : "writing RKE2 additional-server config");
        await executor.UploadTextAsync(
            connection, configYaml, "/etc/rancher/rke2/config.yaml",
            mode: UnixFileMode.UserRead | UnixFileMode.UserWrite, useSudo: true, progress: progress, ct: ct);

        var installScript = BuildInstallCommand(remoteArtifactDir, "server");
        progress?.Report("running RKE2 install script (server)");
        var installOutcome = await executor.ExecuteAsync(
            connection, new NodeCommand { CommandLine = installScript, UseSudo = true, Timeout = TimeSpan.FromMinutes(5) }, progress, ct);
        if (!installOutcome.Ok)
        {
            throw new InvalidOperationException($"RKE2 server install failed: {installOutcome.StandardError}");
        }

        progress?.Report("enabling rke2-server service");
        var enableOutcome = await executor.ExecuteAsync(
            connection, new NodeCommand { CommandLine = "systemctl enable --now rke2-server", UseSudo = true, Timeout = TimeSpan.FromMinutes(3) }, progress, ct);
        if (!enableOutcome.Ok)
        {
            throw new InvalidOperationException($"failed to start rke2-server: {enableOutcome.StandardError}");
        }

        await WaitForNodeReadyAsync(connection, executor, "rke2-server", progress, ct);
    }

    private static async Task WaitForNodeReadyAsync(
        NodeConnection connection, INodeExecutor executor, string serviceName, IProgress<string>? progress, CancellationToken ct)
    {
        const int maxAttempts = 60;
        const string readinessCheck =
            "export PATH=$PATH:/var/lib/rancher/rke2/bin:/usr/local/bin && " +
            "export KUBECONFIG=/etc/rancher/rke2/rke2.yaml && " +
            "NODE_NAME=$(hostname) && " +
            "rke2 kubectl get node \"$NODE_NAME\" --no-headers 2>/dev/null | awk '{print $2}' | grep -qx Ready && echo READY || echo NOTREADY";

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
        $"INSTALL_RKE2_ARTIFACT_PATH={ShellCommand.Quote(remoteArtifactDir)} INSTALL_RKE2_TYPE={installType} sh {ShellCommand.Quote($"{remoteArtifactDir}/install.sh")}";

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
                "firewall-cmd --permanent --add-port=9345/tcp",
                "firewall-cmd --permanent --add-port=10250/tcp",
                "firewall-cmd --permanent --add-port=2379-2380/tcp",
                "firewall-cmd --permanent --add-port=8472/udp",
                "firewall-cmd --reload"),
            FirewallKind.Ufw => string.Join(" && ",
                "ufw allow 6443/tcp",
                "ufw allow 9345/tcp",
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
