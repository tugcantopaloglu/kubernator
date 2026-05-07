using Kubernator.Core.Abstractions;
using Kubernator.Core.Pipelines;
using Kubernator.Core.Strategy;
using Kubernator.Core.Updates;
using Kubernator.Web.Downloads;
using Microsoft.AspNetCore.Mvc;

namespace Kubernator.Web.Api.Controllers;

[ApiController]
[Route("api/v1/pipeline")]
[Produces("application/json")]
[Tags("Generation")]
public sealed class PipelineController : ControllerBase
{
    private readonly IAnalysisService analysis;
    private readonly IStrategySelector strategy;
    private readonly IPipelineService pipelines;
    private readonly ArtifactRegistry artifacts;
    private readonly ILogger<PipelineController> logger;

    public PipelineController(
        IAnalysisService analysis,
        IStrategySelector strategy,
        IPipelineService pipelines,
        ArtifactRegistry artifacts,
        ILogger<PipelineController> logger)
    {
        this.analysis = analysis;
        this.strategy = strategy;
        this.pipelines = pipelines;
        this.artifacts = artifacts;
        this.logger = logger;
    }

    [HttpPost]
    [ProducesResponseType(typeof(PipelineResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiProblem), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiProblem), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PipelineResponse>> Generate([FromBody] PipelineRequest request, CancellationToken ct)
    {
        var path = ApiPathHelpers.ResolveExistingPath(request?.Path, "path");
        ApiPathHelpers.RequireField(request!.Target, "target");

        var target = request.Target.ToLowerInvariant() switch
        {
            "gh-actions" or "github" or "githubactions" => PipelineTarget.GitHubActions,
            "gitlab" or "gitlab-ci" or "gitlabci" => PipelineTarget.GitLabCi,
            "azure" or "azure-devops" or "azuredevops" => PipelineTarget.AzureDevOps,
            "tekton" => PipelineTarget.Tekton,
            _ => throw ApiException.BadRequest("invalid target", "expected: gh-actions, gitlab, azure-devops, tekton")
        };

        var descriptor = await analysis.AnalyzeAsync(path, ct);
        var plan = strategy.Plan(descriptor, new StrategyOptions
        {
            ImageName = string.IsNullOrWhiteSpace(request.ImageName) ? null : request.ImageName,
            ImageTag = string.IsNullOrWhiteSpace(request.ImageTag) ? null : request.ImageTag
        });
        var output = ApiPathHelpers.ResolveOutputDirectory(request.OutputDirectory, "pipeline");

        logger.LogInformation("pipeline {Path} -> {Output} target={Target}", path, output, target);

        var options = new PipelineOptions
        {
            AppKind = descriptor.Kind,
            ImageName = plan.ImageName,
            ImageTag = plan.ImageTag,
            Registry = request.Registry ?? "registry.example.com",
            Namespace = request.Namespace ?? "default",
            PublishPath = request.PublishPath ?? "./publish",
            BundleArtifactName = request.BundleArtifactName ?? "bundle",
            SignBundle = request.SignBundle ?? false,
            RunVerify = request.RunVerify ?? true,
            KubernatorVersion = KubernatorVersion.Current
        };
        var result = await pipelines.GenerateAsync(target, options, output, ct);

        string? token = null;
        string? url = null;
        if (request.ReturnDownloadToken)
        {
            token = artifacts.RegisterDirectory(output, $"kubernator-pipeline-{target.ToString().ToLowerInvariant()}");
            url = $"/download/{token}";
        }

        return Ok(new PipelineResponse
        {
            Target = target.ToString(),
            WrittenFiles = result.WrittenFiles,
            DownloadToken = token,
            DownloadUrl = url
        });
    }
}
