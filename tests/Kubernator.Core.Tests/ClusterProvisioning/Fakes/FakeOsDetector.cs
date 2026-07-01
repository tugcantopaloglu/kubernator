using Kubernator.Core.ClusterProvisioning.Os;
using Kubernator.Core.ClusterProvisioning.Ssh;

namespace Kubernator.Core.Tests.ClusterProvisioning.Fakes;

internal sealed class FakeOsDetector : IOsDetector
{
    public OsFacts Default { get; set; } = new()
    {
        Family = OsFamily.DebianLike,
        DistroId = "ubuntu",
        VersionId = "22.04",
        Arch = "amd64",
        SelinuxEnforcing = false,
        Firewall = FirewallKind.Ufw,
        SwapEnabled = false
    };

    public Dictionary<string, OsFacts> ByHost { get; } = new(StringComparer.Ordinal);

    public Task<OsFacts> DetectAsync(NodeConnection connection, INodeExecutor executor, CancellationToken ct = default)
    {
        if (connection.Host is not null && ByHost.TryGetValue(connection.Host, out var facts))
        {
            return Task.FromResult(facts);
        }
        return Task.FromResult(Default);
    }
}
