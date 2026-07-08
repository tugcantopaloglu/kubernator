using System.Text;
using Kubernator.Core.ClusterProvisioning.Distros;
using Kubernator.Core.ClusterProvisioning.Os;
using Kubernator.Core.ClusterProvisioning.Ssh;
using Kubernator.Core.ClusterProvisioning.Topology;
using Kubernator.Core.ClusterProvisioning.Upgrade;
using Kubernator.Core.Deployment;
using Kubernator.Core.Monitoring;
using Kubernator.Core.Validation;

namespace Kubernator.Core.ClusterProvisioning;

public sealed class ClusterProvisioningService : IClusterProvisioningService
{
    private readonly INodeExecutor executor;
    private readonly IOsDetector osDetector;
    private readonly IReadOnlyDictionary<DistroKind, IClusterDistroProvisioner> provisioners;
    private readonly IClusterMonitor clusterMonitor;
    private readonly IClusterApplier clusterApplier;
    private readonly IProcessRunner processRunner;
    private readonly ClusterUpgradePlanner upgradePlanner;

    public ClusterProvisioningService(
        INodeExecutor executor,
        IOsDetector osDetector,
        IEnumerable<IClusterDistroProvisioner> provisioners,
        IClusterMonitor clusterMonitor,
        IClusterApplier clusterApplier,
        IProcessRunner processRunner,
        ClusterUpgradePlanner upgradePlanner)
    {
        this.executor = executor;
        this.osDetector = osDetector;
        this.provisioners = provisioners.ToDictionary(p => p.Kind);
        this.clusterMonitor = clusterMonitor;
        this.clusterApplier = clusterApplier;
        this.processRunner = processRunner;
        this.upgradePlanner = upgradePlanner;
    }

    public async Task<ClusterProvisionResult> InstallAsync(
        ClusterProvisionOptions options,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var completedSteps = new List<string>();
        var topology = options.Topology;

        var validation = ClusterTopologyValidator.Validate(topology);
        if (!validation.Ok)
        {
            return new ClusterProvisionResult { Ok = false, Errors = validation.Errors, CompletedSteps = completedSteps };
        }
        foreach (var warning in validation.Warnings)
        {
            progress?.Report($"warning: {warning}");
        }

        if (ClusterContext.LooksLikeProduction(topology.ClusterName) && !options.AllowProduction)
        {
            return new ClusterProvisionResult
            {
                Ok = false,
                Errors = [$"cluster name '{topology.ClusterName}' looks like a production cluster — pass AllowProduction=true (CLI: --allow-production) to proceed"],
                CompletedSteps = completedSteps
            };
        }

        if (!provisioners.TryGetValue(topology.Distro, out var provisioner))
        {
            return new ClusterProvisionResult
            {
                Ok = false,
                Errors = [$"no provisioner registered for distro '{topology.Distro}'"],
                CompletedSteps = completedSteps
            };
        }

        try
        {
            var remoteArtifactDirs = new Dictionary<string, string>(StringComparer.Ordinal);

            progress?.Report("preparing nodes (OS detection, prerequisites, artifacts)");
            using (var prepGate = new SemaphoreSlim(Math.Max(1, options.MaxParallelAgents)))
            {
                var prepTasks = topology.Nodes.Select(async node =>
                {
                    await prepGate.WaitAsync(ct);
                    try
                    {
                        var reachable = await executor.TestConnectionAsync(node.Connection, ct);
                        if (!reachable)
                        {
                            throw new InvalidOperationException($"node '{node.Name}' is not reachable");
                        }

                        var os = await osDetector.DetectAsync(node.Connection, executor, ct);
                        await provisioner.PrepareOsAsync(node.Connection, executor, os, topology.PermissiveFirewall, progress, ct);
                        var remoteDir = await provisioner.PrepareArtifactAsync(
                            node.Connection, executor, topology.LocalArtifactBundlePath, os, topology.Version, progress, ct);

                        lock (remoteArtifactDirs)
                        {
                            remoteArtifactDirs[node.Name] = remoteDir;
                        }
                    }
                    finally
                    {
                        prepGate.Release();
                    }
                });
                await Task.WhenAll(prepTasks);
            }
            completedSteps.Add("node preparation complete");

            var servers = topology.Nodes.Where(n => n.Role == NodeRole.Server).ToList();
            var agents = topology.Nodes.Where(n => n.Role == NodeRole.Agent).ToList();
            var initNode = servers.Single(n => n.IsInitServer);

            var registrationHost = topology.FixedRegistrationAddresses.Count > 0
                ? topology.FixedRegistrationAddresses[0]
                : ResolveAddress(initNode);

            var tlsSans = topology.FixedRegistrationAddresses
                .Concat(servers.Select(ResolveAddress))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            progress?.Report($"bootstrapping first server: {initNode.Name}");
            await provisioner.BootstrapFirstServerAsync(initNode.Connection, executor, remoteArtifactDirs[initNode.Name], new ServerBootstrapOptions
            {
                ClusterName = topology.ClusterName,
                Version = topology.Version,
                TlsSans = tlsSans,
                AdvertiseAddress = ResolveAddress(initNode),
                CniPlugin = topology.CniPlugin,
                PodCidr = topology.PodCidr,
                CalicoEncapsulation = topology.CalicoEncapsulation,
                PermissiveFirewall = topology.PermissiveFirewall,
                IsFirstServer = true
            }, progress, ct);
            completedSteps.Add($"first server '{initNode.Name}' bootstrapped");

            var token = await provisioner.ReadJoinTokenAsync(initNode.Connection, executor, ct);
            var joinUrl = $"https://{registrationHost}:{provisioner.JoinPort}";

            foreach (var server in servers.Where(n => !n.IsInitServer))
            {
                progress?.Report($"joining additional server: {server.Name}");
                await provisioner.JoinAdditionalServerAsync(server.Connection, executor, remoteArtifactDirs[server.Name], new ServerBootstrapOptions
                {
                    ClusterName = topology.ClusterName,
                    Version = topology.Version,
                    TlsSans = tlsSans,
                    AdvertiseAddress = ResolveAddress(server),
                    CniPlugin = topology.CniPlugin,
                    PodCidr = topology.PodCidr,
                    CalicoEncapsulation = topology.CalicoEncapsulation,
                    PermissiveFirewall = topology.PermissiveFirewall,
                    IsFirstServer = false,
                    JoinServerUrl = joinUrl,
                    Token = token
                }, progress, ct);
                completedSteps.Add($"server '{server.Name}' joined");
            }

            if (agents.Count > 0)
            {
                using var agentGate = new SemaphoreSlim(Math.Max(1, options.MaxParallelAgents));
                var agentTasks = agents.Select(async agent =>
                {
                    await agentGate.WaitAsync(ct);
                    try
                    {
                        progress?.Report($"joining agent: {agent.Name}");
                        await provisioner.JoinAgentAsync(agent.Connection, executor, remoteArtifactDirs[agent.Name], new AgentJoinOptions
                        {
                            JoinServerUrl = joinUrl,
                            Token = token,
                            PermissiveFirewall = topology.PermissiveFirewall
                        }, progress, ct);
                        lock (completedSteps)
                        {
                            completedSteps.Add($"agent '{agent.Name}' joined");
                        }
                    }
                    finally
                    {
                        agentGate.Release();
                    }
                });
                await Task.WhenAll(agentTasks);
            }

            var contextName = options.KubeconfigContextName ?? topology.ClusterName;
            if (options.RegisterKubeconfigContext)
            {
                progress?.Report("registering kubeconfig context");
                await RegisterKubeconfigAsync(provisioner, initNode, registrationHost, contextName, options.KubectlBinary, progress, ct);
                completedSteps.Add($"kubeconfig context '{contextName}' registered");
            }

            progress?.Report("verifying cluster health");
            var snapshot = await clusterMonitor.GetSnapshotAsync(new ClusterMonitorOptions
            {
                Context = contextName,
                KubectlBinary = options.KubectlBinary
            }, ct);
            completedSteps.Add($"cluster verification: {snapshot.ReadyNodes}/{topology.Nodes.Count} nodes ready");

            return new ClusterProvisionResult { Ok = true, Errors = [], CompletedSteps = completedSteps };
        }
        catch (Exception ex)
        {
            return new ClusterProvisionResult { Ok = false, Errors = [ex.Message], CompletedSteps = completedSteps };
        }
    }

    public async Task<ClusterUpgradeResult> UpgradeAsync(
        ClusterProvisionOptions options,
        string targetVersion,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var topology = options.Topology;

        if (ClusterContext.LooksLikeProduction(topology.ClusterName) && !options.AllowProduction)
        {
            return new ClusterUpgradeResult
            {
                Ok = false,
                Errors = [$"cluster name '{topology.ClusterName}' looks like a production cluster — pass AllowProduction=true (CLI: --allow-production) to proceed"],
                UpgradedNodes = [],
                SkippedNodes = []
            };
        }

        if (!provisioners.TryGetValue(topology.Distro, out var provisioner))
        {
            return new ClusterUpgradeResult
            {
                Ok = false,
                Errors = [$"no provisioner registered for distro '{topology.Distro}'"],
                UpgradedNodes = [],
                SkippedNodes = []
            };
        }

        var plan = await upgradePlanner.PlanAsync(topology, targetVersion, ct);
        var contextName = options.KubeconfigContextName ?? topology.ClusterName;

        var upgraded = new List<string>();
        var skipped = new List<string>();
        var errors = new List<string>();

        foreach (var step in plan.Steps)
        {
            ct.ThrowIfCancellationRequested();
            if (!step.NeedsUpgrade)
            {
                skipped.Add(step.Node.Name);
                progress?.Report($"{step.Node.Name} already at {targetVersion}, skipping");
                continue;
            }

            try
            {
                var remoteDir = await provisioner.PrepareArtifactAsync(
                    step.Node.Connection, executor, topology.LocalArtifactBundlePath, step.Os, targetVersion, progress, ct);

                progress?.Report($"cordoning {step.Node.Name}");
                await processRunner.RunAsync(new ProcessInvocation
                {
                    FileName = options.KubectlBinary,
                    Arguments = ["--context", contextName, "cordon", step.Node.Name],
                    Timeout = TimeSpan.FromSeconds(30)
                }, ct);

                if (step.Node.Role == NodeRole.Agent)
                {
                    await processRunner.RunAsync(new ProcessInvocation
                    {
                        FileName = options.KubectlBinary,
                        Arguments = ["--context", contextName, "drain", step.Node.Name, "--ignore-daemonsets", "--delete-emptydir-data", "--force", "--timeout=120s"],
                        Timeout = TimeSpan.FromMinutes(3)
                    }, ct);
                }

                await provisioner.UpgradeNodeAsync(step.Node.Connection, executor, remoteDir, step.Node.Role, step.Node.IsInitServer, progress, ct);

                progress?.Report($"uncordoning {step.Node.Name}");
                await processRunner.RunAsync(new ProcessInvocation
                {
                    FileName = options.KubectlBinary,
                    Arguments = ["--context", contextName, "uncordon", step.Node.Name],
                    Timeout = TimeSpan.FromSeconds(30)
                }, ct);

                upgraded.Add(step.Node.Name);
            }
            catch (Exception ex)
            {
                errors.Add($"{step.Node.Name}: {ex.Message}");
            }
        }

        return new ClusterUpgradeResult
        {
            Ok = errors.Count == 0,
            Errors = errors,
            UpgradedNodes = upgraded,
            SkippedNodes = skipped
        };
    }

    private async Task RegisterKubeconfigAsync(
        IClusterDistroProvisioner provisioner, NodeSpec initNode, string registrationHost, string contextName, string kubectlBinary,
        IProgress<string>? progress, CancellationToken ct)
    {
        var yaml = await provisioner.ReadKubeconfigAsync(initNode.Connection, executor, ct);
        var caB64 = ExtractYamlValue(yaml, "certificate-authority-data")
            ?? throw new InvalidOperationException("kubeconfig missing certificate-authority-data");
        var certB64 = ExtractYamlValue(yaml, "client-certificate-data")
            ?? throw new InvalidOperationException("kubeconfig missing client-certificate-data");
        var keyB64 = ExtractYamlValue(yaml, "client-key-data")
            ?? throw new InvalidOperationException("kubeconfig missing client-key-data");

        var registration = new ClusterRegistration
        {
            Name = contextName,
            ServerUrl = $"https://{registrationHost}:{provisioner.ApiServerPort}",
            CaCertificatePem = Encoding.UTF8.GetString(Convert.FromBase64String(caB64)),
            ClientCertificatePem = Encoding.UTF8.GetString(Convert.FromBase64String(certB64)),
            ClientKeyPem = Encoding.UTF8.GetString(Convert.FromBase64String(keyB64))
        };

        var result = await clusterApplier.RegisterContextAsync(registration, kubectlBinary, ct);
        if (!result.Ok)
        {
            throw new InvalidOperationException($"failed to register kubeconfig context: {string.Join("; ", result.Errors)}");
        }
        foreach (var step in result.AppliedSteps)
        {
            progress?.Report(step);
        }
    }

    private static string? ExtractYamlValue(string yaml, string key)
    {
        foreach (var rawLine in yaml.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.StartsWith(key + ":", StringComparison.Ordinal))
            {
                return line[(key.Length + 1)..].Trim();
            }
        }
        return null;
    }

    private static string ResolveAddress(NodeSpec node) =>
        node.AdvertiseAddress ?? node.Connection.Host ?? node.Name;
}
