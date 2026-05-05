using Kubernator.Core.Containers;
using Kubernator.Core.Strategy;

namespace Kubernator.Core.Packaging;

public interface IBundleService
{
    Task<BundleResult> CreateAsync(
        BuildPlan plan,
        BundleOptions options,
        IContainerEngine engine,
        IProgress<string>? progress = null,
        CancellationToken ct = default);

    Task<BundleVerificationResult> VerifyAsync(
        string bundlePath,
        IProgress<string>? progress = null,
        CancellationToken ct = default);
}
