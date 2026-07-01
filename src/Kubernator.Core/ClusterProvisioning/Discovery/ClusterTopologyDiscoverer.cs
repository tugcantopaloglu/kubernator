using System.Globalization;
using System.Text.Json;
using Kubernator.Core.ClusterProvisioning.Distros;
using Kubernator.Core.ClusterProvisioning.Ssh;
using Kubernator.Core.ClusterProvisioning.Topology;
using Kubernator.Core.Validation;

namespace Kubernator.Core.ClusterProvisioning.Discovery;

public sealed record ClusterDiscoveryOptions
{
    public string? Context { get; init; }
    public string KubectlBinary { get; init; } = "kubectl";
    public required string ClusterName { get; init; }
    public required DistroKind Distro { get; init; }
    public required string Version { get; init; }
    public required string LocalArtifactBundlePath { get; init; }
    public required string SshUsername { get; init; }
    public string? SshPrivateKeyVaultId { get; init; }
    public string? SshPrivateKeyPath { get; init; }
    public int SshPort { get; init; } = 22;
    public IReadOnlyList<string> FixedRegistrationAddresses { get; init; } = [];
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
}

public sealed record ClusterDiscoveryResult
{
    public required ClusterTopology Topology { get; init; }
    public required IReadOnlyList<string> Errors { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
}

public sealed class ClusterTopologyDiscoverer
{
    private readonly IProcessRunner runner;

    public ClusterTopologyDiscoverer(IProcessRunner runner)
    {
        this.runner = runner;
    }

    public async Task<ClusterDiscoveryResult> DiscoverAsync(ClusterDiscoveryOptions options, CancellationToken ct = default)
    {
        var args = new List<string>();
        if (!string.IsNullOrWhiteSpace(options.Context))
        {
            args.Add("--context");
            args.Add(options.Context);
        }
        args.Add("get");
        args.Add("nodes");
        args.Add("-o");
        args.Add("json");

        var outcome = await runner.RunAsync(new ProcessInvocation
        {
            FileName = options.KubectlBinary,
            Arguments = args,
            Timeout = options.Timeout
        }, ct);

        if (!outcome.Ok)
        {
            throw new InvalidOperationException($"kubectl get nodes failed: {outcome.StandardError}");
        }

        var discovered = ParseNodes(outcome.StandardOutput);
        if (discovered.Count == 0)
        {
            throw new InvalidOperationException("kubectl returned no nodes");
        }

        var oldestServer = discovered
            .Where(n => n.IsServer)
            .OrderBy(n => n.CreationTimestamp)
            .ThenBy(n => n.Name, StringComparer.Ordinal)
            .FirstOrDefault();

        var nodeSpecs = discovered.Select(n => new NodeSpec
        {
            Name = n.Name,
            Role = n.IsServer ? NodeRole.Server : NodeRole.Agent,
            Connection = new NodeConnection
            {
                Mode = NodeConnectionMode.Ssh,
                Host = n.Host,
                Port = options.SshPort,
                Username = options.SshUsername,
                SshPrivateKeyVaultId = options.SshPrivateKeyVaultId,
                SshPrivateKeyPath = options.SshPrivateKeyPath
            },
            IsInitServer = oldestServer is not null && string.Equals(n.Name, oldestServer.Name, StringComparison.Ordinal)
        }).ToList();

        var topology = new ClusterTopology
        {
            ClusterName = options.ClusterName,
            Distro = options.Distro,
            Version = options.Version,
            Nodes = nodeSpecs,
            FixedRegistrationAddresses = options.FixedRegistrationAddresses,
            LocalArtifactBundlePath = options.LocalArtifactBundlePath
        };

        var validation = ClusterTopologyValidator.Validate(topology);

        return new ClusterDiscoveryResult
        {
            Topology = topology,
            Errors = validation.Errors,
            Warnings = validation.Warnings
        };
    }

    private sealed record DiscoveredNode(string Name, bool IsServer, string? Host, DateTimeOffset CreationTimestamp);

    private static IReadOnlyList<DiscoveredNode> ParseNodes(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var result = new List<DiscoveredNode>();
        if (!doc.RootElement.TryGetProperty("items", out var items))
        {
            return result;
        }

        foreach (var item in items.EnumerateArray())
        {
            var metadata = item.GetProperty("metadata");
            var name = metadata.GetProperty("name").GetString()
                ?? throw new InvalidOperationException("node without a name");

            var isServer = false;
            if (metadata.TryGetProperty("labels", out var labels) && labels.ValueKind == JsonValueKind.Object)
            {
                foreach (var label in labels.EnumerateObject())
                {
                    if (label.Name is "node-role.kubernetes.io/control-plane" or "node-role.kubernetes.io/master")
                    {
                        isServer = true;
                        break;
                    }
                }
            }

            var creationTimestamp = metadata.TryGetProperty("creationTimestamp", out var ts) && ts.ValueKind == JsonValueKind.String
                ? DateTimeOffset.Parse(ts.GetString()!, CultureInfo.InvariantCulture)
                : DateTimeOffset.MaxValue;

            string? internalIp = null;
            string? externalIp = null;
            if (item.TryGetProperty("status", out var status)
                && status.TryGetProperty("addresses", out var addresses)
                && addresses.ValueKind == JsonValueKind.Array)
            {
                foreach (var addr in addresses.EnumerateArray())
                {
                    var type = addr.TryGetProperty("type", out var t) ? t.GetString() : null;
                    var address = addr.TryGetProperty("address", out var a) ? a.GetString() : null;
                    if (type == "InternalIP")
                    {
                        internalIp = address;
                    }
                    else if (type == "ExternalIP")
                    {
                        externalIp = address;
                    }
                }
            }

            result.Add(new DiscoveredNode(name, isServer, internalIp ?? externalIp ?? name, creationTimestamp));
        }

        return result;
    }
}
