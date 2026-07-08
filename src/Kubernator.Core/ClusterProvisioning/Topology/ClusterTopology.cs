using System.Text.Json;
using System.Text.Json.Serialization;
using Kubernator.Core.ClusterProvisioning.Distros;
using Kubernator.Core.ClusterProvisioning.Ssh;

namespace Kubernator.Core.ClusterProvisioning.Topology;

public static class ClusterNetworkDefaults
{
    /// <summary>Default pod network CIDR (Flannel's canonical default; also kubeadm's <c>podSubnet</c>).</summary>
    public const string PodCidr = "10.244.0.0/16";
}

public sealed record NodeSpec
{
    public required string Name { get; init; }
    public required NodeRole Role { get; init; }
    public required NodeConnection Connection { get; init; }
    public string? AdvertiseAddress { get; init; }
    public bool IsInitServer { get; init; }
}

public sealed record ClusterTopology
{
    public required string ClusterName { get; init; }
    public required DistroKind Distro { get; init; }
    public required string Version { get; init; }
    public required IReadOnlyList<NodeSpec> Nodes { get; init; }
    public IReadOnlyList<string> FixedRegistrationAddresses { get; init; } = [];
    public string CniPlugin { get; init; } = "canal";
    public string PodCidr { get; init; } = ClusterNetworkDefaults.PodCidr;
    /// <summary>Calico dataplane mode: <c>"bgp"</c> (default; BIRD + IPIP) or <c>"vxlan"</c>. Ignored for non-Calico CNIs.</summary>
    public string CalicoEncapsulation { get; init; } = "bgp";
    public bool PermissiveFirewall { get; init; }
    public required string LocalArtifactBundlePath { get; init; }
}

public static class ClusterTopologyJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static ClusterTopology Parse(string json) =>
        JsonSerializer.Deserialize<ClusterTopology>(json, Options)
            ?? throw new InvalidOperationException("topology file is empty or invalid");

    public static async Task<ClusterTopology> LoadAsync(string path, CancellationToken ct = default)
    {
        var json = await File.ReadAllTextAsync(path, ct);
        return Parse(json);
    }

    public static string Serialize(ClusterTopology topology) => JsonSerializer.Serialize(topology, Options);

    public static async Task SaveAsync(string path, ClusterTopology topology, CancellationToken ct = default) =>
        await File.WriteAllTextAsync(path, Serialize(topology), ct);
}
