using Kubernator.Core.Deployment;
using Kubernator.Web.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kubernator.Web.Api.Controllers;

[ApiController]
[Route("api/v1/deploy")]
[Produces("application/json")]
[Tags("Deployment")]
[Authorize(Policy = ApiKeyScopes.GeneratePolicy)]
public sealed class DeployController : ControllerBase
{
    private readonly IClusterApplier applier;
    private readonly ILogger<DeployController> logger;

    public DeployController(IClusterApplier applier, ILogger<DeployController> logger)
    {
        this.applier = applier;
        this.logger = logger;
    }

    [HttpGet("contexts")]
    [ProducesResponseType(typeof(ContextListResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ContextListResponse>> ListContexts([FromQuery] string? kubectlBinary, CancellationToken ct)
    {
        var binary = string.IsNullOrWhiteSpace(kubectlBinary) ? "kubectl" : kubectlBinary;
        var contexts = await applier.ListContextsAsync(binary, ct);
        return Ok(new ContextListResponse
        {
            Contexts = contexts.Select(c => new ContextDto
            {
                Name = c.Name,
                IsCurrent = c.IsCurrent,
                LooksProduction = c.LooksProduction
            }).ToArray()
        });
    }

    [HttpPost]
    [ProducesResponseType(typeof(DeployResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiProblem), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiProblem), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiProblem), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DeployResultDto>> Apply([FromBody] DeployRequest request, CancellationToken ct)
    {
        var manifests = ApiPathHelpers.ResolveExistingDirectory(request?.ManifestsDirectory, "manifestsDirectory");
        ApiPathHelpers.RequireField(request!.Context, "context");

        if (ClusterContext.LooksLikeProduction(request.Context) && !request.AllowProduction && !request.DryRun)
        {
            throw ApiException.Conflict(
                "production context refused",
                $"context '{request.Context}' looks like production; pass allowProduction=true or dryRun=true");
        }

        logger.LogInformation("deploy {Manifests} → context={Context} ns={Namespace} dryRun={DryRun}",
            manifests, request.Context, request.Namespace ?? "default", request.DryRun);

        var options = new DeployOptions
        {
            ManifestsDirectory = manifests,
            Context = request.Context,
            Namespace = request.Namespace ?? "default",
            DryRun = request.DryRun,
            AllowProduction = request.AllowProduction,
            CreateNamespace = request.CreateNamespace,
            KubectlBinary = request.KubectlBinary ?? "kubectl"
        };

        var result = await applier.ApplyAsync(options, progress: null, ct);
        return Ok(DeployResultDto.From(result));
    }
}
