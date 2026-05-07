using Kubernator.Core.Abstractions;
using Kubernator.Core.Strategy;
using Kubernator.Core.Validation;
using Kubernator.Web.Auth;
using Kubernator.Web.Jobs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kubernator.Web.Api.Controllers;

[ApiController]
[Route("api/v1/validate")]
[Produces("application/json")]
[Tags("Build")]
[Authorize(Policy = ApiKeyScopes.GeneratePolicy)]
public sealed class ValidateController : ControllerBase
{
    private readonly IServiceScopeFactory scopeFactory;
    private readonly IJobManager jobs;
    private readonly ILogger<ValidateController> logger;

    public ValidateController(IServiceScopeFactory scopeFactory, IJobManager jobs, ILogger<ValidateController> logger)
    {
        this.scopeFactory = scopeFactory;
        this.jobs = jobs;
        this.logger = logger;
    }

    [HttpPost]
    [ProducesResponseType(typeof(JobAcceptedResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ApiProblem), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiProblem), StatusCodes.Status404NotFound)]
    public ActionResult<JobAcceptedResponse> Submit([FromBody] ValidateRequest request)
    {
        var path = ApiPathHelpers.ResolveExistingPath(request?.Path, "path");
        var keyId = User.FindFirst(ApiKeyScopes.KeyIdClaimType)?.Value;
        var keyName = User.FindFirst(ApiKeyScopes.KeyNameClaimType)?.Value;
        var captured = request! with { Path = path };
        var sf = scopeFactory;

        var record = jobs.Enqueue(new JobSubmission
        {
            Kind = "validate",
            KeyId = keyId,
            KeyName = keyName,
            Work = async (ctx, ct) =>
            {
                using var scope = sf.CreateScope();
                var analysis = scope.ServiceProvider.GetRequiredService<IAnalysisService>();
                var strategy = scope.ServiceProvider.GetRequiredService<IStrategySelector>();
                var validator = scope.ServiceProvider.GetRequiredService<IValidator>();

                ctx.Report($"analyzing {captured.Path}");
                var descriptor = await analysis.AnalyzeAsync(captured.Path, ct);
                var plan = strategy.Plan(descriptor, new StrategyOptions
                {
                    ImageName = string.IsNullOrWhiteSpace(captured.ImageName) ? null : captured.ImageName,
                    ImageTag = string.IsNullOrWhiteSpace(captured.ImageTag) ? null : captured.ImageTag
                });

                var manifestsDir = Path.Combine(captured.Path, ".kubernator", "kubernetes");
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
                    ClusterName = captured.ClusterName ?? "kubernator-test",
                    Namespace = captured.Namespace ?? "default",
                    DeleteClusterOnComplete = !captured.KeepCluster,
                    ReuseExistingCluster = captured.ReuseExistingCluster,
                    HttpProbePath = captured.ProbePath,
                    HttpProbePort = captured.ProbePort
                };
                var result = await validator.ValidateAsync(options, ctx.AsProgress(), ct);
                return ValidateResultDto.From(result);
            }
        });
        logger.LogInformation("validate job {Id} submitted by key={KeyId}", record.Id, keyId);
        return Accepted($"/api/v1/jobs/{record.Id}", new JobAcceptedResponse
        {
            Id = record.Id,
            Kind = record.Kind,
            Status = record.Status.ToString(),
            Location = $"/api/v1/jobs/{record.Id}"
        });
    }
}
