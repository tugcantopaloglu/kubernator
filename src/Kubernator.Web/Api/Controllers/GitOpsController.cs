using Kubernator.Core.Abstractions;
using Kubernator.Core.GitOps;
using Kubernator.Core.Strategy;
using Kubernator.Web.Downloads;
using Microsoft.AspNetCore.Mvc;

namespace Kubernator.Web.Api.Controllers;

[ApiController]
[Route("api/v1/gitops")]
[Produces("application/json")]
[Tags("Generation")]
public sealed class GitOpsController : ControllerBase
{
    private readonly IAnalysisService analysis;
    private readonly IStrategySelector strategy;
    private readonly IGitOpsService gitOps;
    private readonly ArtifactRegistry artifacts;
    private readonly ILogger<GitOpsController> logger;

    public GitOpsController(
        IAnalysisService analysis,
        IStrategySelector strategy,
        IGitOpsService gitOps,
        ArtifactRegistry artifacts,
        ILogger<GitOpsController> logger)
    {
        this.analysis = analysis;
        this.strategy = strategy;
        this.gitOps = gitOps;
        this.artifacts = artifacts;
        this.logger = logger;
    }

    [HttpPost]
    [ProducesResponseType(typeof(GitOpsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiProblem), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiProblem), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GitOpsResponse>> Generate([FromBody] GitOpsRequest request, CancellationToken ct)
    {
        var path = ApiPathHelpers.ResolveExistingPath(request?.Path, "path");
        ApiPathHelpers.RequireField(request!.RepoUrl, "repoUrl");

        var descriptor = await analysis.AnalyzeAsync(path, ct);
        var plan = strategy.Plan(descriptor, new StrategyOptions
        {
            ImageName = string.IsNullOrWhiteSpace(request.ImageName) ? null : request.ImageName,
            ImageTag = string.IsNullOrWhiteSpace(request.ImageTag) ? null : request.ImageTag
        });
        var output = ApiPathHelpers.ResolveOutputDirectory(request.OutputDirectory, "gitops");

        var sourceKind = request.SourceKind switch
        {
            null => GitOpsSourceKind.Directory,
            "" => GitOpsSourceKind.Directory,
            _ when Enum.TryParse<GitOpsSourceKind>(request.SourceKind, true, out var parsed) => parsed,
            _ => throw ApiException.BadRequest("invalid sourceKind", "expected: Directory, Helm, Kustomize")
        };

        logger.LogInformation("gitops {Path} -> {Output} repo={Repo} kind={Kind}",
            path, output, request.RepoUrl, sourceKind);

        var options = new GitOpsOptions
        {
            OutputDirectory = output,
            RepoUrl = request.RepoUrl,
            TargetRevision = request.TargetRevision ?? "HEAD",
            SourcePath = request.SourcePath ?? ".",
            SourceKind = sourceKind,
            DestinationServer = request.DestinationServer ?? "https://kubernetes.default.svc",
            DestinationNamespace = request.DestinationNamespace ?? "default",
            ArgoNamespace = request.ArgoNamespace ?? "argocd",
            ApplicationName = request.ApplicationName,
            ProjectName = request.ProjectName,
            AutomatedSync = request.AutomatedSync ?? true,
            SelfHeal = request.SelfHeal ?? true,
            Prune = request.Prune ?? true,
            CreateNamespace = request.CreateNamespace ?? true
        };
        var result = await gitOps.GenerateAsync(plan, options, ct);

        string? token = null;
        string? url = null;
        if (request.ReturnDownloadToken)
        {
            token = artifacts.RegisterDirectory(output, "kubernator-gitops");
            url = $"/download/{token}";
        }

        return Ok(new GitOpsResponse
        {
            OutputDirectory = result.OutputDirectory,
            WrittenFiles = result.WrittenFiles,
            DownloadToken = token,
            DownloadUrl = url
        });
    }
}
