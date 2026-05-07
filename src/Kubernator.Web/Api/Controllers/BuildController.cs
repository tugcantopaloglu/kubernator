using Kubernator.Web.Auth;
using Kubernator.Web.Jobs;
using Kubernator.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kubernator.Web.Api.Controllers;

[ApiController]
[Route("api/v1/build")]
[Produces("application/json")]
[Tags("Build")]
[Authorize(Policy = ApiKeyScopes.GeneratePolicy)]
public sealed class BuildController : ControllerBase
{
    private readonly IServiceScopeFactory scopeFactory;
    private readonly IJobManager jobs;
    private readonly ILogger<BuildController> logger;

    public BuildController(IServiceScopeFactory scopeFactory, IJobManager jobs, ILogger<BuildController> logger)
    {
        this.scopeFactory = scopeFactory;
        this.jobs = jobs;
        this.logger = logger;
    }

    [HttpPost]
    [ProducesResponseType(typeof(JobAcceptedResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ApiProblem), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiProblem), StatusCodes.Status404NotFound)]
    public ActionResult<JobAcceptedResponse> Submit([FromBody] BuildRequest request)
    {
        var path = ApiPathHelpers.ResolveExistingPath(request?.Path, "path");
        var output = string.IsNullOrWhiteSpace(request!.OutputDirectory)
            ? null
            : Path.GetFullPath(request.OutputDirectory);

        var keyId = User.FindFirst(ApiKeyScopes.KeyIdClaimType)?.Value;
        var keyName = User.FindFirst(ApiKeyScopes.KeyNameClaimType)?.Value;
        var captured = request with { Path = path, OutputDirectory = output };
        var sf = scopeFactory;

        var record = jobs.Enqueue(new JobSubmission
        {
            Kind = "build",
            KeyId = keyId,
            KeyName = keyName,
            Work = async (ctx, ct) =>
            {
                using var scope = sf.CreateScope();
                var pipeline = scope.ServiceProvider.GetRequiredService<BuildPipeline>();
                ctx.Report($"build {captured.Path} → {captured.OutputDirectory ?? "<auto>"}");
                var result = await pipeline.RunAsync(new BuildPipelineRequest
                {
                    Path = captured.Path,
                    OutputDirectory = captured.OutputDirectory,
                    ImageName = captured.ImageName,
                    ImageTag = captured.ImageTag,
                    NoBuild = captured.NoBuild,
                    Platforms = captured.Platforms
                }, ctx.AsProgress(), ct);
                return BuildResultDto.From(result);
            }
        });
        logger.LogInformation("build job {Id} submitted by key={KeyId}", record.Id, keyId);
        return Accepted($"/api/v1/jobs/{record.Id}", new JobAcceptedResponse
        {
            Id = record.Id,
            Kind = record.Kind,
            Status = record.Status.ToString(),
            Location = $"/api/v1/jobs/{record.Id}"
        });
    }
}
