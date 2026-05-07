using Kubernator.Core.Abstractions;
using Kubernator.Core.Kustomize;
using Kubernator.Core.Strategy;
using Kubernator.Web.Auth;
using Kubernator.Web.Downloads;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kubernator.Web.Api.Controllers;

[ApiController]
[Route("api/v1/kustomize")]
[Produces("application/json")]
[Tags("Generation")]
[Authorize(Policy = ApiKeyScopes.GeneratePolicy)]
public sealed class KustomizeController : ControllerBase
{
    private readonly IAnalysisService analysis;
    private readonly IStrategySelector strategy;
    private readonly IKustomizeService kustomize;
    private readonly ArtifactRegistry artifacts;
    private readonly ILogger<KustomizeController> logger;

    public KustomizeController(
        IAnalysisService analysis,
        IStrategySelector strategy,
        IKustomizeService kustomize,
        ArtifactRegistry artifacts,
        ILogger<KustomizeController> logger)
    {
        this.analysis = analysis;
        this.strategy = strategy;
        this.kustomize = kustomize;
        this.artifacts = artifacts;
        this.logger = logger;
    }

    [HttpPost]
    [ProducesResponseType(typeof(KustomizeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiProblem), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiProblem), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<KustomizeResponse>> Generate([FromBody] KustomizeRequest request, CancellationToken ct)
    {
        var path = ApiPathHelpers.ResolveExistingPath(request?.Path, "path");
        var descriptor = await analysis.AnalyzeAsync(path, ct);
        var plan = strategy.Plan(descriptor, new StrategyOptions
        {
            ImageName = string.IsNullOrWhiteSpace(request!.ImageName) ? null : request.ImageName,
            ImageTag = string.IsNullOrWhiteSpace(request.ImageTag) ? null : request.ImageTag,
            Exposure = request.Exposure?.ToCore()
        });
        var output = ApiPathHelpers.ResolveOutputDirectory(request.OutputDirectory, "kustomize");

        var overlays = (request.Overlays is { Count: > 0 })
            ? request.Overlays.Where(o => !string.IsNullOrWhiteSpace(o)).ToArray()
            : new[] { "production" };

        logger.LogInformation("kustomize {Path} -> {Output} overlays={Overlays}", path, output, string.Join(",", overlays));

        var options = new KustomizeOptions
        {
            OutputDirectory = output,
            BaseNamespace = request.Namespace,
            Overlays = overlays,
            Replicas = request.Replicas <= 0 ? 1 : request.Replicas,
            Scaling = request.Scaling?.ToCore(),
            Exposure = request.Exposure?.ToCore()
        };
        var result = await kustomize.GenerateAsync(plan, options, ct);

        string? token = null;
        string? url = null;
        if (request.ReturnDownloadToken)
        {
            token = artifacts.RegisterDirectory(output, "kubernator-kustomize");
            url = $"/download/{token}";
        }

        return Ok(new KustomizeResponse
        {
            BaseDirectory = result.BaseDirectory,
            WrittenFiles = result.WrittenFiles,
            DownloadToken = token,
            DownloadUrl = url
        });
    }
}
