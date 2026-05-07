using Kubernator.Core.Abstractions;
using Kubernator.Core.Generation;
using Kubernator.Core.Strategy;
using Kubernator.Web.Downloads;
using Microsoft.AspNetCore.Mvc;

namespace Kubernator.Web.Api.Controllers;

[ApiController]
[Route("api/v1/generate")]
[Produces("application/json")]
[Tags("Generation")]
public sealed class GenerateController : ControllerBase
{
    private readonly IAnalysisService analysis;
    private readonly IStrategySelector strategy;
    private readonly IGenerationService generation;
    private readonly ArtifactRegistry artifacts;
    private readonly ILogger<GenerateController> logger;

    public GenerateController(
        IAnalysisService analysis,
        IStrategySelector strategy,
        IGenerationService generation,
        ArtifactRegistry artifacts,
        ILogger<GenerateController> logger)
    {
        this.analysis = analysis;
        this.strategy = strategy;
        this.generation = generation;
        this.artifacts = artifacts;
        this.logger = logger;
    }

    [HttpPost]
    [ProducesResponseType(typeof(GenerateResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiProblem), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiProblem), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiProblem), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<GenerateResponse>> Generate([FromBody] GenerateRequest request, CancellationToken ct)
    {
        var path = ApiPathHelpers.ResolveExistingPath(request?.Path, "path");
        var descriptor = await analysis.AnalyzeAsync(path, ct);
        var plan = strategy.Plan(descriptor, new StrategyOptions
        {
            ImageName = string.IsNullOrWhiteSpace(request!.ImageName) ? null : request.ImageName,
            ImageTag = string.IsNullOrWhiteSpace(request.ImageTag) ? null : request.ImageTag,
            Exposure = request.Exposure?.ToCore()
        });
        var output = ApiPathHelpers.ResolveOutputDirectory(request.OutputDirectory, "generate");

        logger.LogInformation("generate {Path} -> {Output} replicas={Replicas} ns={Namespace}",
            path, output, request.Replicas, request.Namespace ?? "<default>");

        var options = new GenerationOptions
        {
            OutputDirectory = output,
            Namespace = request.Namespace,
            Replicas = request.Replicas <= 0 ? 1 : request.Replicas,
            CpuRequest = request.CpuRequest ?? "100m",
            CpuLimit = request.CpuLimit ?? "1000m",
            MemoryRequest = request.MemoryRequest ?? "128Mi",
            MemoryLimit = request.MemoryLimit ?? "512Mi",
            Scaling = request.Scaling?.ToCore(),
            OverwriteExisting = true
        };
        var result = await generation.GenerateAsync(plan, options, ct);

        string? token = null;
        string? url = null;
        if (request.ReturnDownloadToken)
        {
            token = artifacts.RegisterDirectory(output, $"kubernator-generate-{descriptor.Kind}");
            url = $"/download/{token}";
        }

        return Ok(new GenerateResponse
        {
            OutputDirectory = output,
            WrittenFiles = result.WrittenFiles,
            ImageReference = plan.FullImageReference,
            BaseImage = plan.RuntimeImage.Reference,
            DownloadToken = token,
            DownloadUrl = url
        });
    }
}
