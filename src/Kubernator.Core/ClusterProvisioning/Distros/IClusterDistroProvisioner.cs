using Kubernator.Core.ClusterProvisioning.Os;
using Kubernator.Core.ClusterProvisioning.Ssh;

namespace Kubernator.Core.ClusterProvisioning.Distros;

public enum DistroKind
{
    Rke2,
    K3s,
    KubeadmNative
}

public enum NodeRole
{
    Server,
    Agent
}

public sealed record ServerBootstrapOptions
{
    public required string ClusterName { get; init; }
    public required string Version { get; init; }
    public required IReadOnlyList<string> TlsSans { get; init; }
    public required string AdvertiseAddress { get; init; }
    public string CniPlugin { get; init; } = "canal";
    public bool PermissiveFirewall { get; init; }
    public bool IsFirstServer { get; init; }
    public string? JoinServerUrl { get; init; }
    public string? Token { get; init; }
}

public sealed record AgentJoinOptions
{
    public required string JoinServerUrl { get; init; }
    public required string Token { get; init; }
    public bool PermissiveFirewall { get; init; }
}

public sealed record NodeVersionInfo
{
    public required bool Installed { get; init; }
    public string? Version { get; init; }
    public NodeRole? Role { get; init; }
}

public interface IClusterDistroProvisioner
{
    DistroKind Kind { get; }

    /// <summary>Port the Kubernetes API server listens on (used to build the final kubeconfig server URL).</summary>
    int ApiServerPort { get; }

    /// <summary>Port additional servers/agents connect to in order to join the cluster (may differ from <see cref="ApiServerPort"/>, e.g. RKE2's separate supervisor port).</summary>
    int JoinPort { get; }

    Task<string> ReadKubeconfigAsync(NodeConnection connection, INodeExecutor executor, CancellationToken ct = default);

    Task PrepareOsAsync(
        NodeConnection connection, INodeExecutor executor, OsFacts os, bool permissiveFirewall,
        IProgress<string>? progress = null, CancellationToken ct = default);

    Task<string> PrepareArtifactAsync(
        NodeConnection connection, INodeExecutor executor, string localBundlePath, OsFacts os, string version,
        IProgress<string>? progress = null, CancellationToken ct = default);

    Task BootstrapFirstServerAsync(
        NodeConnection connection, INodeExecutor executor, string remoteArtifactDir, ServerBootstrapOptions options,
        IProgress<string>? progress = null, CancellationToken ct = default);

    Task JoinAdditionalServerAsync(
        NodeConnection connection, INodeExecutor executor, string remoteArtifactDir, ServerBootstrapOptions options,
        IProgress<string>? progress = null, CancellationToken ct = default);

    Task JoinAgentAsync(
        NodeConnection connection, INodeExecutor executor, string remoteArtifactDir, AgentJoinOptions options,
        IProgress<string>? progress = null, CancellationToken ct = default);

    Task<string> ReadJoinTokenAsync(NodeConnection connection, INodeExecutor executor, CancellationToken ct = default);

    Task<NodeVersionInfo> GetInstalledVersionAsync(NodeConnection connection, INodeExecutor executor, CancellationToken ct = default);

    Task UpgradeNodeAsync(
        NodeConnection connection, INodeExecutor executor, string remoteArtifactDir, NodeRole role,
        IProgress<string>? progress = null, CancellationToken ct = default);
}
