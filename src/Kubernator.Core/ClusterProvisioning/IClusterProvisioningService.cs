using Kubernator.Core.ClusterProvisioning.Upgrade;

namespace Kubernator.Core.ClusterProvisioning;

public interface IClusterProvisioningService
{
    Task<ClusterProvisionResult> InstallAsync(
        ClusterProvisionOptions options,
        IProgress<string>? progress = null,
        CancellationToken ct = default);

    Task<ClusterUpgradeResult> UpgradeAsync(
        ClusterProvisionOptions options,
        string targetVersion,
        IProgress<string>? progress = null,
        CancellationToken ct = default);
}
