using System.Text;
using System.Text.Json;
using Kubernator.Core.ClusterProvisioning.Os;
using Kubernator.Core.ClusterProvisioning.Ssh;
using Kubernator.Core.ClusterProvisioning.Topology;

namespace Kubernator.Core.ClusterProvisioning.Distros.Kubeadm;

public sealed class KubeadmDistroProvisioner : IClusterDistroProvisioner
{
    private const string KubeconfigPath = "/etc/kubernetes/admin.conf";
    private const string ContainerdVersion = "1.7.22";
    private const string RuncVersion = "1.1.14";
    private const string CniPluginsVersion = "1.5.1";

    private static readonly UnixFileMode ExecutableMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
        UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
        UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

    private const string ContainerdServiceUnit = """
        [Unit]
        Description=containerd container runtime
        After=network.target

        [Service]
        ExecStartPre=-/sbin/modprobe overlay
        ExecStart=/usr/local/bin/containerd
        Restart=always
        RestartSec=5
        Delegate=yes
        KillMode=process
        OOMScoreAdjust=-999
        LimitNOFILE=infinity
        LimitNPROC=infinity
        LimitCORE=infinity
        TasksMax=infinity

        [Install]
        WantedBy=multi-user.target
        """;

    public DistroKind Kind => DistroKind.KubeadmNative;

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
            throw new InvalidOperationException($"failed to read kubeadm kubeconfig: {outcome.StandardError}");
        }
        return outcome.StandardOutput;
    }

    public async Task PrepareOsAsync(
        NodeConnection connection, INodeExecutor executor, OsFacts os, bool permissiveFirewall,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        progress?.Report("preparing OS prerequisites for kubeadm");

        var script = new StringBuilder();
        script.AppendLine("modprobe overlay || true");
        script.AppendLine("modprobe br_netfilter || true");
        script.AppendLine("cat <<'EOF' > /etc/modules-load.d/kubeadm.conf");
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
        var artifactsDir = Path.Combine(localBundlePath, "artifacts", arch);
        var kubeadmPath = Path.Combine(artifactsDir, "kubeadm");
        var kubeletPath = Path.Combine(artifactsDir, "kubelet");
        var kubectlPath = Path.Combine(artifactsDir, "kubectl");
        var containerdTar = Path.Combine(artifactsDir, $"containerd-{ContainerdVersion}-linux-{arch}.tar.gz");
        var runcPath = Path.Combine(artifactsDir, $"runc.{arch}");
        var cniPluginsTar = Path.Combine(artifactsDir, $"cni-plugins-linux-{arch}-v{CniPluginsVersion}.tgz");
        var imagesDir = Path.Combine(localBundlePath, "images", arch);
        var cniDir = Path.Combine(localBundlePath, "cni");

        if (!File.Exists(kubeadmPath) || !File.Exists(kubeletPath) || !File.Exists(kubectlPath)
            || !File.Exists(containerdTar) || !File.Exists(runcPath) || !File.Exists(cniPluginsTar))
        {
            throw new InvalidOperationException($"artifact bundle at {localBundlePath} is missing required files for arch {arch}");
        }

        var remoteDir = $"/opt/kubernator/artifacts/{version}";
        progress?.Report($"staging kubeadm {version} artifacts ({arch}) on node");

        await executor.UploadFileAsync(connection, kubeadmPath, "/usr/local/bin/kubeadm", mode: ExecutableMode, useSudo: true, progress: progress, ct: ct);
        await executor.UploadFileAsync(connection, kubeletPath, "/usr/local/bin/kubelet", mode: ExecutableMode, useSudo: true, progress: progress, ct: ct);
        await executor.UploadFileAsync(connection, kubectlPath, "/usr/local/bin/kubectl", mode: ExecutableMode, useSudo: true, progress: progress, ct: ct);
        await executor.UploadFileAsync(connection, runcPath, "/usr/local/sbin/runc", mode: ExecutableMode, useSudo: true, progress: progress, ct: ct);

        var remoteContainerdTar = $"{remoteDir}/{Path.GetFileName(containerdTar)}";
        await executor.UploadFileAsync(connection, containerdTar, remoteContainerdTar, useSudo: true, progress: progress, ct: ct);
        var extractContainerd = await executor.ExecuteAsync(
            connection, new NodeCommand { CommandLine = $"tar -C /usr/local -xzf {ShellCommand.Quote(remoteContainerdTar)}", UseSudo = true, Timeout = TimeSpan.FromMinutes(2) }, progress, ct);
        if (!extractContainerd.Ok)
        {
            throw new InvalidOperationException($"failed to extract containerd: {extractContainerd.StandardError}");
        }

        var remoteCniTar = $"{remoteDir}/{Path.GetFileName(cniPluginsTar)}";
        await executor.UploadFileAsync(connection, cniPluginsTar, remoteCniTar, useSudo: true, progress: progress, ct: ct);
        var extractCni = await executor.ExecuteAsync(
            connection,
            new NodeCommand { CommandLine = $"mkdir -p /opt/cni/bin && tar -C /opt/cni/bin -xzf {ShellCommand.Quote(remoteCniTar)}", UseSudo = true, Timeout = TimeSpan.FromMinutes(2) },
            progress, ct);
        if (!extractCni.Ok)
        {
            throw new InvalidOperationException($"failed to extract CNI plugins: {extractCni.StandardError}");
        }

        await executor.UploadTextAsync(connection, ContainerdServiceUnit, "/etc/systemd/system/containerd.service", useSudo: true, progress: progress, ct: ct);
        var configureContainerd = await executor.ExecuteAsync(
            connection,
            new NodeCommand
            {
                CommandLine = "mkdir -p /etc/containerd && containerd config default > /etc/containerd/config.toml && " +
                              "sed -i 's/SystemdCgroup = false/SystemdCgroup = true/' /etc/containerd/config.toml && " +
                              "systemctl daemon-reload && systemctl enable --now containerd",
                UseSudo = true,
                Timeout = TimeSpan.FromMinutes(2)
            },
            progress, ct);
        if (!configureContainerd.Ok)
        {
            throw new InvalidOperationException($"failed to start containerd: {configureContainerd.StandardError}");
        }

        if (Directory.Exists(imagesDir))
        {
            var imageTars = Directory.GetFiles(imagesDir, "*.tar");
            if (imageTars.Length > 0)
            {
                var remoteImagesDir = $"{remoteDir}/images";
                foreach (var tarPath in imageTars)
                {
                    await executor.UploadFileAsync(connection, tarPath, $"{remoteImagesDir}/{Path.GetFileName(tarPath)}", useSudo: true, progress: progress, ct: ct);
                }

                progress?.Report("importing container images into containerd");
                var importOutcome = await executor.ExecuteAsync(
                    connection,
                    new NodeCommand
                    {
                        CommandLine = $"for f in {ShellCommand.Quote(remoteImagesDir)}/*.tar; do ctr -n k8s.io images import \"$f\"; done",
                        UseSudo = true,
                        Timeout = TimeSpan.FromMinutes(5)
                    },
                    progress, ct);
                if (!importOutcome.Ok)
                {
                    throw new InvalidOperationException($"failed to import container images: {importOutcome.StandardError}");
                }
            }
        }

        if (Directory.Exists(cniDir))
        {
            foreach (var manifest in new[] { "flannel.yaml", "calico.yaml" })
            {
                var localManifest = Path.Combine(cniDir, manifest);
                if (File.Exists(localManifest))
                {
                    await executor.UploadFileAsync(connection, localManifest, $"{remoteDir}/cni/{manifest}", useSudo: true, progress: progress, ct: ct);
                }
            }
        }

        return remoteDir;
    }

    public async Task BootstrapFirstServerAsync(
        NodeConnection connection, INodeExecutor executor, string remoteArtifactDir, ServerBootstrapOptions options,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var configYaml = KubeadmConfigTemplate.RenderInit(options);
        progress?.Report("writing kubeadm init config");
        await executor.UploadTextAsync(
            connection, configYaml, "/etc/kubernetes/kubeadm-init.yaml",
            mode: UnixFileMode.UserRead | UnixFileMode.UserWrite, useSudo: true, progress: progress, ct: ct);

        progress?.Report("running kubeadm init");
        var initOutcome = await executor.ExecuteAsync(
            connection,
            new NodeCommand { CommandLine = "kubeadm init --config /etc/kubernetes/kubeadm-init.yaml --upload-certs", UseSudo = true, Timeout = TimeSpan.FromMinutes(10) },
            progress, ct);
        if (!initOutcome.Ok)
        {
            throw new InvalidOperationException($"kubeadm init failed: {initOutcome.StandardError}");
        }

        var cniManifest = options.CniPlugin == "calico" ? "calico.yaml" : "flannel.yaml";
        var manifestPath = $"{remoteArtifactDir}/cni/{cniManifest}";

        // The vendored CNI manifests ship with the shipped defaults baked in; rewrite them in
        // place only for the knobs the operator changed, leaving the default path byte-for-byte
        // as before.
        foreach (var patchCommand in BuildCniPatchCommands(options, manifestPath))
        {
            progress?.Report($"patching {options.CniPlugin} manifest");
            var patchOutcome = await executor.ExecuteAsync(
                connection,
                new NodeCommand { CommandLine = patchCommand, UseSudo = true, Timeout = TimeSpan.FromSeconds(30) },
                progress, ct);
            if (!patchOutcome.Ok)
            {
                throw new InvalidOperationException($"failed to patch CNI manifest: {patchOutcome.StandardError}");
            }
        }

        progress?.Report($"applying {options.CniPlugin} CNI manifest");
        var applyOutcome = await executor.ExecuteAsync(
            connection,
            new NodeCommand
            {
                CommandLine = $"kubectl --kubeconfig {KubeconfigPath} apply -f {ShellCommand.Quote(manifestPath)}",
                UseSudo = true,
                Timeout = TimeSpan.FromMinutes(2)
            },
            progress, ct);
        if (!applyOutcome.Ok)
        {
            throw new InvalidOperationException($"failed to apply CNI manifest: {applyOutcome.StandardError}");
        }

        await WaitForNodeReadyAsync(connection, executor, progress, ct);
    }

    public async Task JoinAdditionalServerAsync(
        NodeConnection connection, INodeExecutor executor, string remoteArtifactDir, ServerBootstrapOptions options,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var configYaml = KubeadmConfigTemplate.RenderJoinControlPlane(options);
        progress?.Report("writing kubeadm join config (control-plane)");
        await executor.UploadTextAsync(
            connection, configYaml, "/etc/kubernetes/kubeadm-join.yaml",
            mode: UnixFileMode.UserRead | UnixFileMode.UserWrite, useSudo: true, progress: progress, ct: ct);

        progress?.Report("running kubeadm join (control-plane)");
        var joinOutcome = await executor.ExecuteAsync(
            connection,
            new NodeCommand { CommandLine = "kubeadm join --config /etc/kubernetes/kubeadm-join.yaml", UseSudo = true, Timeout = TimeSpan.FromMinutes(10) },
            progress, ct);
        if (!joinOutcome.Ok)
        {
            throw new InvalidOperationException($"kubeadm join (control-plane) failed: {joinOutcome.StandardError}");
        }

        await WaitForNodeReadyAsync(connection, executor, progress, ct);
    }

    public async Task JoinAgentAsync(
        NodeConnection connection, INodeExecutor executor, string remoteArtifactDir, AgentJoinOptions options,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var configYaml = KubeadmConfigTemplate.RenderJoinWorker(options);
        progress?.Report("writing kubeadm join config (worker)");
        await executor.UploadTextAsync(
            connection, configYaml, "/etc/kubernetes/kubeadm-join.yaml",
            mode: UnixFileMode.UserRead | UnixFileMode.UserWrite, useSudo: true, progress: progress, ct: ct);

        progress?.Report("running kubeadm join (worker)");
        var joinOutcome = await executor.ExecuteAsync(
            connection,
            new NodeCommand { CommandLine = "kubeadm join --config /etc/kubernetes/kubeadm-join.yaml", UseSudo = true, Timeout = TimeSpan.FromMinutes(10) },
            progress, ct);
        if (!joinOutcome.Ok)
        {
            throw new InvalidOperationException($"kubeadm join (worker) failed: {joinOutcome.StandardError}");
        }
    }

    public async Task<string> ReadJoinTokenAsync(NodeConnection connection, INodeExecutor executor, CancellationToken ct = default)
    {
        var tokenOutcome = await executor.ExecuteAsync(
            connection,
            new NodeCommand { CommandLine = "kubeadm token create --print-join-command", UseSudo = true, Timeout = TimeSpan.FromSeconds(30) },
            null, ct);
        if (!tokenOutcome.Ok)
        {
            throw new InvalidOperationException($"failed to create kubeadm join token: {tokenOutcome.StandardError}");
        }

        var (bootstrapToken, caCertHash) = ParseJoinCommand(tokenOutcome.StandardOutput);

        var certKeyOutcome = await executor.ExecuteAsync(
            connection,
            new NodeCommand { CommandLine = "kubeadm init phase upload-certs --upload-certs", UseSudo = true, Timeout = TimeSpan.FromSeconds(30) },
            null, ct);
        if (!certKeyOutcome.Ok)
        {
            throw new InvalidOperationException($"failed to mint kubeadm certificate-key: {certKeyOutcome.StandardError}");
        }

        var certificateKey = ParseCertificateKey(certKeyOutcome.StandardOutput);
        return KubeadmJoinToken.Encode(bootstrapToken, caCertHash, certificateKey);
    }

    public async Task<NodeVersionInfo> GetInstalledVersionAsync(NodeConnection connection, INodeExecutor executor, CancellationToken ct = default)
    {
        const string script = """
            if command -v kubeadm >/dev/null 2>&1; then
                kubeadm version -o json
            else
                echo NOT_INSTALLED
            fi
            if [ -f /etc/kubernetes/admin.conf ]; then
                echo ROLE=server
            elif [ -f /etc/kubernetes/kubelet.conf ]; then
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
        var roleLine = lines.FirstOrDefault(l => l.StartsWith("ROLE=", StringComparison.Ordinal));
        var versionLine = lines.FirstOrDefault(l => !l.StartsWith("ROLE=", StringComparison.Ordinal));

        if (versionLine is null || versionLine == "NOT_INSTALLED" || versionLine == "ROLE=none")
        {
            return new NodeVersionInfo { Installed = false };
        }

        NodeRole? role = roleLine switch
        {
            "ROLE=server" => NodeRole.Server,
            "ROLE=agent" => NodeRole.Agent,
            _ => null
        };

        string? version = null;
        try
        {
            using var doc = JsonDocument.Parse(versionLine);
            version = doc.RootElement.GetProperty("clientVersion").GetProperty("gitVersion").GetString();
        }
        catch (JsonException)
        {
            return new NodeVersionInfo { Installed = false };
        }

        return new NodeVersionInfo { Installed = version is not null, Version = version, Role = role };
    }

    public async Task UpgradeNodeAsync(
        NodeConnection connection, INodeExecutor executor, string remoteArtifactDir, NodeRole role, bool isInitServer,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        await UploadUpgradedBinariesAsync(connection, executor, remoteArtifactDir, progress, ct);

        if (role == NodeRole.Agent)
        {
            progress?.Report("restarting kubelet");
            var restartOutcome = await executor.ExecuteAsync(
                connection, new NodeCommand { CommandLine = "systemctl restart kubelet", UseSudo = true, Timeout = TimeSpan.FromMinutes(2) }, progress, ct);
            if (!restartOutcome.Ok)
            {
                throw new InvalidOperationException($"failed to restart kubelet: {restartOutcome.StandardError}");
            }
            return;
        }

        // The init control-plane node drives the cluster-wide upgrade with `kubeadm upgrade apply`
        // (run exactly once); every other control-plane node then reconciles with `kubeadm upgrade node`.
        // The caller guarantees the init server is upgraded first (see ClusterUpgradePlanner ordering).
        var targetVersion = ExtractVersionFromArtifactDir(remoteArtifactDir);

        var upgradeCommand = isInitServer
            ? $"kubeadm upgrade apply {ShellCommand.Quote(targetVersion)} -y"
            : "kubeadm upgrade node";

        progress?.Report(isInitServer ? "running kubeadm upgrade apply" : "running kubeadm upgrade node");
        var upgradeOutcome = await executor.ExecuteAsync(
            connection, new NodeCommand { CommandLine = upgradeCommand, UseSudo = true, Timeout = TimeSpan.FromMinutes(10) }, progress, ct);
        if (!upgradeOutcome.Ok)
        {
            throw new InvalidOperationException($"kubeadm upgrade failed: {upgradeOutcome.StandardError}");
        }

        progress?.Report("restarting kubelet");
        var kubeletOutcome = await executor.ExecuteAsync(
            connection, new NodeCommand { CommandLine = "systemctl restart kubelet", UseSudo = true, Timeout = TimeSpan.FromMinutes(2) }, progress, ct);
        if (!kubeletOutcome.Ok)
        {
            throw new InvalidOperationException($"failed to restart kubelet: {kubeletOutcome.StandardError}");
        }

        await WaitForNodeReadyAsync(connection, executor, progress, ct);
    }

    private static async Task UploadUpgradedBinariesAsync(
        NodeConnection connection, INodeExecutor executor, string remoteArtifactDir, IProgress<string>? progress, CancellationToken ct)
    {
        progress?.Report("staging upgraded kubeadm/kubelet/kubectl binaries");
        foreach (var binary in new[] { "kubeadm", "kubelet", "kubectl" })
        {
            var stageOutcome = await executor.ExecuteAsync(
                connection,
                new NodeCommand
                {
                    CommandLine = $"cp {ShellCommand.Quote($"{remoteArtifactDir}/{binary}")} /usr/local/bin/{binary} && chmod 0755 /usr/local/bin/{binary}",
                    UseSudo = true,
                    Timeout = TimeSpan.FromSeconds(30)
                },
                progress, ct);
            if (!stageOutcome.Ok)
            {
                throw new InvalidOperationException($"failed to stage upgraded {binary}: {stageOutcome.StandardError}");
            }
        }
    }

    private static string ExtractVersionFromArtifactDir(string remoteArtifactDir) =>
        remoteArtifactDir[(remoteArtifactDir.LastIndexOf('/') + 1)..];

    // Builds the in-place `sed` edits that reconcile the vendored CNI manifest with the topology.
    // Emits nothing for the shipped defaults, so the default install path is unchanged.
    internal static IReadOnlyList<string> BuildCniPatchCommands(ServerBootstrapOptions options, string manifestPath)
    {
        var quotedPath = ShellCommand.Quote(manifestPath);
        var commands = new List<string>();

        var customCidr = !string.Equals(options.PodCidr, ClusterNetworkDefaults.PodCidr, StringComparison.Ordinal);
        if (customCidr)
        {
            // Flannel: rewrite the canonical 10.244.0.0/16 in net-conf.json's "Network".
            // Calico: uncomment and set the CALICO_IPV4POOL_CIDR env var (default 192.168.0.0/16).
            commands.Add(options.CniPlugin == "calico"
                ? $"sed -i -e 's|# - name: CALICO_IPV4POOL_CIDR|- name: CALICO_IPV4POOL_CIDR|' " +
                  $"-e 's|#   value: \"192.168.0.0/16\"|  value: \"{options.PodCidr}\"|' {quotedPath}"
                : $"sed -i 's|{ClusterNetworkDefaults.PodCidr}|{options.PodCidr}|g' {quotedPath}");
        }

        // Calico defaults to a BGP (BIRD) backend with IPIP encapsulation. Switching to VXLAN
        // disables BIRD (calico_backend) and flips the default IP pool's encapsulation env vars.
        // The `{n;s}` sed ranges edit the value line that follows each env-var name line, so the
        // substitution can't accidentally hit an unrelated "Never"/"Always" elsewhere.
        if (options.CniPlugin == "calico" && string.Equals(options.CalicoEncapsulation, "vxlan", StringComparison.OrdinalIgnoreCase))
        {
            commands.Add(
                $"sed -i -e 's|calico_backend: \"bird\"|calico_backend: \"vxlan\"|' " +
                $"-e '/name: CALICO_IPV4POOL_VXLAN/{{n;s/\"Never\"/\"Always\"/}}' " +
                $"-e '/name: CALICO_IPV4POOL_IPIP/{{n;s/\"Always\"/\"Never\"/}}' {quotedPath}");
        }

        return commands;
    }

    private static (string BootstrapToken, string CaCertHash) ParseJoinCommand(string output)
    {
        var tokenMatch = System.Text.RegularExpressions.Regex.Match(output, @"--token\s+(\S+)");
        var hashMatch = System.Text.RegularExpressions.Regex.Match(output, @"--discovery-token-ca-cert-hash\s+sha256:(\S+)");
        if (!tokenMatch.Success || !hashMatch.Success)
        {
            throw new InvalidOperationException($"could not parse kubeadm join command output: {output}");
        }
        return (tokenMatch.Groups[1].Value, hashMatch.Groups[1].Value);
    }

    private static string ParseCertificateKey(string output)
    {
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var key = lines.LastOrDefault(l => System.Text.RegularExpressions.Regex.IsMatch(l, "^[0-9a-fA-F]{64}$"));
        if (key is null)
        {
            throw new InvalidOperationException($"could not parse kubeadm certificate-key output: {output}");
        }
        return key;
    }

    private static async Task WaitForNodeReadyAsync(NodeConnection connection, INodeExecutor executor, IProgress<string>? progress, CancellationToken ct)
    {
        const int maxAttempts = 60;
        const string readinessCheck =
            "NODE_NAME=$(hostname) && " +
            "kubectl --kubeconfig " + KubeconfigPath + " get node \"$NODE_NAME\" --no-headers 2>/dev/null | awk '{print $2}' | grep -qx Ready && echo READY || echo NOTREADY";

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var check = await executor.ExecuteAsync(
                connection, new NodeCommand { CommandLine = readinessCheck, UseSudo = true, Timeout = TimeSpan.FromSeconds(15) }, null, ct);

            if (check.Ok && check.StandardOutput.Contains("READY", StringComparison.Ordinal))
            {
                progress?.Report("node is Ready");
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(5), ct);
        }

        throw new InvalidOperationException("timed out waiting for node to become Ready");
    }

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
                "firewall-cmd --permanent --add-port=2379-2380/tcp",
                "firewall-cmd --permanent --add-port=10250/tcp",
                "firewall-cmd --permanent --add-port=10259/tcp",
                "firewall-cmd --permanent --add-port=10257/tcp",
                "firewall-cmd --permanent --add-port=8472/udp",
                "firewall-cmd --permanent --add-port=179/tcp",
                "firewall-cmd --reload"),
            FirewallKind.Ufw => string.Join(" && ",
                "ufw allow 6443/tcp",
                "ufw allow 2379:2380/tcp",
                "ufw allow 10250/tcp",
                "ufw allow 10259/tcp",
                "ufw allow 10257/tcp",
                "ufw allow 8472/udp",
                "ufw allow 179/tcp"),
            _ => ": # no firewall manager detected"
        };
    }
}
