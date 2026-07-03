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
    private readonly IJobManager jobs;
    private readonly ILogger<ValidateController> logger;

    public ValidateController(IJobManager jobs, ILogger<ValidateController> logger)
    {
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

        var record = jobs.Enqueue("validate", captured, keyId, keyName);
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
