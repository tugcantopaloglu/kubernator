using Kubernator.Core.ClusterProvisioning.Ssh;

namespace Kubernator.Core.ClusterProvisioning.Os;

public interface IOsDetector
{
    Task<OsFacts> DetectAsync(NodeConnection connection, INodeExecutor executor, CancellationToken ct = default);
}
