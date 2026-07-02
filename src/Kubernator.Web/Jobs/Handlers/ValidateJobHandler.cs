using Kubernator.Core.Abstractions;
using Kubernator.Core.Strategy;
using Kubernator.Core.Validation;
using Kubernator.Web.Api;

namespace Kubernator.Web.Jobs.Handlers;

public sealed class ValidateJobHandler(IServiceScopeFactory scopeFactory) : JobHandler<ValidateRequest>
{
    public override string Kind => "validate";

    protected override async Task<object?> RunAsync(ValidateRequest payload, JobContext ctx, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var analysis = scope.ServiceProvider.GetRequiredService<IAnalysisService>();
        var strategy = scope.ServiceProvider.GetRequiredService<IStrategySelector>();
        var validator = scope.ServiceProvider.GetRequiredService<IValidator>();

        ctx.Report($"analyzing {payload.Path}");
        var descriptor = await analysis.AnalyzeAsync(payload.Path, ct);
        var plan = strategy.Plan(descriptor, new StrategyOptions
        {
            ImageName = string.IsNullOrWhiteSpace(payload.ImageName) ? null : payload.ImageName,
            ImageTag = string.IsNullOrWhiteSpace(payload.ImageTag) ? null : payload.ImageTag
        });

        var manifestsDir = Path.Combine(payload.Path, ".kubernator", "kubernetes");
        if (!Directory.Exists(manifestsDir))
        {
            throw new InvalidOperationException(
                $"manifests not found at {manifestsDir}; run /api/v1/generate first");
        }

        var options = new ValidationOptions
        {
            ManifestsDirectory = manifestsDir,
            ImageReference = plan.FullImageReference,
            DeploymentName = plan.ImageName,
            ClusterName = payload.ClusterName ?? "kubernator-test",
            Namespace = payload.Namespace ?? "default",
            DeleteClusterOnComplete = !payload.KeepCluster,
            ReuseExistingCluster = payload.ReuseExistingCluster,
            HttpProbePath = payload.ProbePath,
            HttpProbePort = payload.ProbePort
        };
        var result = await validator.ValidateAsync(options, ctx.AsProgress(), ct);
        return ValidateResultDto.From(result);
    }
}
