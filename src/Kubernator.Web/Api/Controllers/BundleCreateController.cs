using Kubernator.Core.Abstractions;
using Kubernator.Core.Containers;
using Kubernator.Core.Packaging;
using Kubernator.Core.Strategy;
using Kubernator.Web.Auth;
using Kubernator.Web.Jobs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kubernator.Web.Api.Controllers;

[ApiController]
[Route("api/v1/bundles")]
[Produces("application/json")]
[Tags("Bundles")]
[Authorize(Policy = ApiKeyScopes.GeneratePolicy)]
public sealed class BundleCreateController : ControllerBase
{
    private readonly IServiceScopeFactory scopeFactory;
    private readonly IJobManager jobs;
    private readonly ILogger<BundleCreateController> logger;

    public BundleCreateController(IServiceScopeFactory scopeFactory, IJobManager jobs, ILogger<BundleCreateController> logger)
    {
        this.scopeFactory = scopeFactory;
        this.jobs = jobs;
        this.logger = logger;
    }

    [HttpPost]
    [ProducesResponseType(typeof(JobAcceptedResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ApiProblem), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiProblem), StatusCodes.Status404NotFound)]
    public ActionResult<JobAcceptedResponse> Create([FromBody] BundleCreateRequest request)
    {
        var path = ApiPathHelpers.ResolveExistingPath(request?.Path, "path");
        ApiPathHelpers.RequireField(request!.OutputBundlePath, "outputBundlePath");
        var bundleOut = Path.GetFullPath(request.OutputBundlePath);
        var bundleDir = Path.GetDirectoryName(bundleOut);
        if (!string.IsNullOrEmpty(bundleDir))
        {
            Directory.CreateDirectory(bundleDir);
        }

        var keyId = User.FindFirst(ApiKeyScopes.KeyIdClaimType)?.Value;
        var keyName = User.FindFirst(ApiKeyScopes.KeyNameClaimType)?.Value;
        var captured = request with { Path = path, OutputBundlePath = bundleOut };
        var sf = scopeFactory;

        var record = jobs.Enqueue(new JobSubmission
        {
            Kind = "bundle",
            KeyId = keyId,
            KeyName = keyName,
            Work = async (ctx, ct) =>
            {
                using var scope = sf.CreateScope();
                var analysis = scope.ServiceProvider.GetRequiredService<IAnalysisService>();
                var strategy = scope.ServiceProvider.GetRequiredService<IStrategySelector>();
                var bundleService = scope.ServiceProvider.GetRequiredService<IBundleService>();
                var engineProvider = scope.ServiceProvider.GetRequiredService<IContainerEngineProvider>();

                ctx.Report($"analyzing {captured.Path}");
                var descriptor = await analysis.AnalyzeAsync(captured.Path, ct);
                var plan = strategy.Plan(descriptor, new StrategyOptions
                {
                    ImageName = string.IsNullOrWhiteSpace(captured.ImageName) ? null : captured.ImageName,
                    ImageTag = string.IsNullOrWhiteSpace(captured.ImageTag) ? null : captured.ImageTag
                });

                ctx.Report("resolving container engine");
                var engine = await engineProvider.ResolveAsync(false, ct);

                var scratch = Path.Combine(Path.GetTempPath(), $"kubernator-bundle-{Guid.NewGuid():N}");
                Directory.CreateDirectory(scratch);

                var options = new BundleOptions
                {
                    OutputBundlePath = captured.OutputBundlePath,
                    ScratchDirectory = scratch,
                    IncludeSbom = captured.IncludeSbom,
                    KubernetesNamespace = captured.Namespace,
                    Replicas = captured.Replicas <= 0 ? 1 : captured.Replicas,
                    BuildIfMissing = captured.BuildIfMissing,
                    KeepScratch = captured.KeepScratch
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
        });
        logger.LogInformation("bundle job {Id} submitted by key={KeyId}", record.Id, keyId);
        return Accepted($"/api/v1/jobs/{record.Id}", new JobAcceptedResponse
        {
            Id = record.Id,
            Kind = record.Kind,
            Status = record.Status.ToString(),
            Location = $"/api/v1/jobs/{record.Id}"
        });
    }
}
