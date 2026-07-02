using Kubernator.Core.Abstractions;
using Kubernator.Core.Containers;
using Kubernator.Core.Packaging;
using Kubernator.Core.Strategy;
using Kubernator.Web.Api;

namespace Kubernator.Web.Jobs.Handlers;

public sealed class BundleCreateJobHandler(IServiceScopeFactory scopeFactory) : JobHandler<BundleCreateRequest>
{
    public override string Kind => "bundle";

    protected override async Task<object?> RunAsync(BundleCreateRequest payload, JobContext ctx, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var analysis = scope.ServiceProvider.GetRequiredService<IAnalysisService>();
        var strategy = scope.ServiceProvider.GetRequiredService<IStrategySelector>();
        var bundleService = scope.ServiceProvider.GetRequiredService<IBundleService>();
        var engineProvider = scope.ServiceProvider.GetRequiredService<IContainerEngineProvider>();

        ctx.Report($"analyzing {payload.Path}");
        var descriptor = await analysis.AnalyzeAsync(payload.Path, ct);
        var plan = strategy.Plan(descriptor, new StrategyOptions
        {
            ImageName = string.IsNullOrWhiteSpace(payload.ImageName) ? null : payload.ImageName,
            ImageTag = string.IsNullOrWhiteSpace(payload.ImageTag) ? null : payload.ImageTag
        });

        ctx.Report("resolving container engine");
        var engine = await engineProvider.ResolveAsync(false, ct);

        var scratch = Path.Combine(Path.GetTempPath(), $"kubernator-bundle-{Guid.NewGuid():N}");
        Directory.CreateDirectory(scratch);

        var bundleDir = Path.GetDirectoryName(payload.OutputBundlePath);
        if (!string.IsNullOrEmpty(bundleDir))
        {
            Directory.CreateDirectory(bundleDir);
        }

        var options = new BundleOptions
        {
            OutputBundlePath = payload.OutputBundlePath,
            ScratchDirectory = scratch,
            IncludeSbom = payload.IncludeSbom,
            KubernetesNamespace = payload.Namespace,
            Replicas = payload.Replicas <= 0 ? 1 : payload.Replicas,
            BuildIfMissing = payload.BuildIfMissing,
            KeepScratch = payload.KeepScratch
        };

        var result = await bundleService.CreateAsync(plan, options, engine, ctx.AsProgress(), ct);
        return new BundleCreateResultDto
        {
            BundlePath = result.BundlePath,
            BundleSizeBytes = result.BundleSizeBytes,
            BundleSha256 = result.BundleSha256,
            ImageReference = plan.FullImageReference
        };
    }
}
