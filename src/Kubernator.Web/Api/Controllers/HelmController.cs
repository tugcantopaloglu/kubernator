using Kubernator.Core.Abstractions;
using Kubernator.Core.Helm;
using Kubernator.Core.Strategy;
using Kubernator.Web.Downloads;
using Microsoft.AspNetCore.Mvc;

namespace Kubernator.Web.Api.Controllers;

[ApiController]
[Route("api/v1/helm")]
[Produces("application/json")]
[Tags("Generation")]
public sealed class HelmController : ControllerBase
{
    private readonly IAnalysisService analysis;
    private readonly IStrategySelector strategy;
    private readonly IHelmService helm;
    private readonly ArtifactRegistry artifacts;
    private readonly ILogger<HelmController> logger;

    public HelmController(
        IAnalysisService analysis,
        IStrategySelector strategy,
        IHelmService helm,
        ArtifactRegistry artifacts,
        ILogger<HelmController> logger)
    {
        this.analysis = analysis;
        this.strategy = strategy;
        this.helm = helm;
        this.artifacts = artifacts;
        this.logger = logger;
    }

    [HttpPost]
    [ProducesResponseType(typeof(HelmResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiProblem), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiProblem), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HelmResponse>> Generate([FromBody] HelmRequest request, CancellationToken ct)
    {
        var path = ApiPathHelpers.ResolveExistingPath(request?.Path, "path");
        var descriptor = await analysis.AnalyzeAsync(path, ct);
        var plan = strategy.Plan(descriptor, new StrategyOptions
        {
            ImageName = string.IsNullOrWhiteSpace(request!.ImageName) ? null : request.ImageName,
            ImageTag = string.IsNullOrWhiteSpace(request.ImageTag) ? null : request.ImageTag,
            Exposure = request.Exposure?.ToCore()
        });
        var output = ApiPathHelpers.ResolveOutputDirectory(request.OutputDirectory, "helm");

        logger.LogInformation("helm {Path} -> {Output} chart={Chart}", path, output, request.ChartName ?? "<auto>");

        var options = new HelmOptions
        {
            OutputDirectory = output,
            ChartName = request.ChartName,
            ChartVersion = request.ChartVersion ?? "0.1.0",
            Description = request.Description,
            Namespace = request.Namespace,
            Replicas = request.Replicas <= 0 ? 1 : request.Replicas,
            Scaling = request.Scaling?.ToCore(),
            Exposure = request.Exposure?.ToCore()
        };
        var result = await helm.GenerateAsync(plan, options, ct);

        string? token = null;
        string? url = null;
        if (request.ReturnDownloadToken)
        {
            token = artifacts.RegisterDirectory(result.ChartDirectory, $"kubernator-helm-{request.ChartName ?? "chart"}");
            url = $"/download/{token}";
        }

        return Ok(new HelmResponse
        {
            ChartDirectory = result.ChartDirectory,
            WrittenFiles = result.WrittenFiles,
            DownloadToken = token,
            DownloadUrl = url
        });
    }
}
