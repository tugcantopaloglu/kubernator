namespace Kubernator.Core.ClusterProvisioning.Artifacts;

public interface IClusterArtifactBundleService
{
    Task<ClusterArtifactManifest> PullAsync(ClusterArtifactPullOptions options, IProgress<string>? progress = null, CancellationToken ct = default);

    Task<string> PackAsync(string bundleDirectory, string archivePath, IProgress<string>? progress = null, CancellationToken ct = default);
}
