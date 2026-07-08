using Kubernator.Core.ClusterProvisioning.Distros;
using Kubernator.Core.ClusterProvisioning.Os;
using Kubernator.Core.ClusterProvisioning.Ssh;

namespace Kubernator.Core.Tests.ClusterProvisioning.Fakes;

internal sealed class FakeClusterDistroProvisioner : IClusterDistroProvisioner
{
    private readonly object gate = new();

    public DistroKind Kind { get; init; } = DistroKind.Rke2;
    public int ApiServerPort { get; init; } = 6443;
    public int JoinPort { get; init; } = 9345;
    public string Kubeconfig { get; set; } = """
        apiVersion: v1
        clusters:
        - cluster:
            certificate-authority-data: Q0E=
            server: https://127.0.0.1:6443
          name: default
        users:
        - name: default
          user:
            client-certificate-data: Q0VSVA==
            client-key-data: S0VZ
        """;
    public List<string> Events { get; } = [];
    public string Token { get; set; } = "fake-token";
    public Func<NodeConnection, NodeVersionInfo>? VersionResponder { get; set; }
    public bool FailBootstrap { get; set; }

    public Task<string> ReadKubeconfigAsync(NodeConnection connection, INodeExecutor executor, CancellationToken ct = default) =>
        Task.FromResult(Kubeconfig);

    public Task PrepareOsAsync(
        NodeConnection connection, INodeExecutor executor, OsFacts os, bool permissiveFirewall,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        Record($"PrepareOs:{HostOf(connection)}");
        return Task.CompletedTask;
    }

    public Task<string> PrepareArtifactAsync(
        NodeConnection connection, INodeExecutor executor, string localBundlePath, OsFacts os, string version,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        Record($"PrepareArtifact:{HostOf(connection)}");
        return Task.FromResult($"/opt/kubernator/artifacts/{version}");
    }

    public Task BootstrapFirstServerAsync(
        NodeConnection connection, INodeExecutor executor, string remoteArtifactDir, ServerBootstrapOptions options,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        Record($"BootstrapFirstServer:{HostOf(connection)}");
        if (FailBootstrap)
        {
            throw new InvalidOperationException("simulated bootstrap failure");
        }
        return Task.CompletedTask;
    }

    public Task JoinAdditionalServerAsync(
        NodeConnection connection, INodeExecutor executor, string remoteArtifactDir, ServerBootstrapOptions options,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        Record($"JoinAdditionalServer:{HostOf(connection)}");
        return Task.CompletedTask;
    }

    public Task JoinAgentAsync(
        NodeConnection connection, INodeExecutor executor, string remoteArtifactDir, AgentJoinOptions options,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        Record($"JoinAgent:{HostOf(connection)}");
        return Task.CompletedTask;
    }

    public Task<string> ReadJoinTokenAsync(NodeConnection connection, INodeExecutor executor, CancellationToken ct = default)
    {
        Record($"ReadJoinToken:{HostOf(connection)}");
        return Task.FromResult(Token);
    }

    public Task<NodeVersionInfo> GetInstalledVersionAsync(NodeConnection connection, INodeExecutor executor, CancellationToken ct = default)
    {
        var info = VersionResponder?.Invoke(connection) ?? new NodeVersionInfo { Installed = false };
        return Task.FromResult(info);
    }

    public Task UpgradeNodeAsync(
        NodeConnection connection, INodeExecutor executor, string remoteArtifactDir, NodeRole role, bool isInitServer,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        Record($"Upgrade:{HostOf(connection)}:{(isInitServer ? "init" : role.ToString().ToLowerInvariant())}");
        return Task.CompletedTask;
    }

    private void Record(string evt)
    {
        lock (gate)
        {
            Events.Add(evt);
        }
    }

    private static string HostOf(NodeConnection connection) => connection.Host ?? "local";
}
