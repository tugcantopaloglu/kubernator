using Kubernator.Web.Auth;
using Kubernator.Web.Jobs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kubernator.Web.Api.Controllers;

[ApiController]
[Route("api/v1/jobs")]
[Produces("application/json")]
[Tags("Jobs")]
[Authorize(Policy = ApiKeyScopes.ReadPolicy)]
public sealed class JobsController : ControllerBase
{
    private readonly IJobManager jobs;
    private readonly ILogger<JobsController> logger;

    public JobsController(IJobManager jobs, ILogger<JobsController> logger)
    {
        this.jobs = jobs;
        this.logger = logger;
    }

    [HttpGet]
    [ProducesResponseType(typeof(JobListResponse), StatusCodes.Status200OK)]
    public ActionResult<JobListResponse> List([FromQuery] int? limit)
    {
        var capped = limit is { } l ? Math.Clamp(l, 1, 500) : 100;
        var list = jobs.List(capped);
        return Ok(new JobListResponse { Jobs = list.Select(JobDto.From).ToArray() });
    }

    [HttpGet("{id}")]
    [ProducesResponseType(typeof(JobDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiProblem), StatusCodes.Status404NotFound)]
    public ActionResult<JobDto> Get(string id)
    {
        var record = jobs.Get(id);
        if (record is null) throw ApiException.NotFound("job not found", id);
        return Ok(JobDto.From(record));
    }

    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiProblem), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiProblem), StatusCodes.Status409Conflict)]
    public IActionResult Cancel(string id)
    {
        var existing = jobs.Get(id);
        if (existing is null) throw ApiException.NotFound("job not found", id);
        if (existing.Status is JobStatus.Succeeded or JobStatus.Failed or JobStatus.Cancelled)
        {
            throw ApiException.Conflict("job already finished", existing.Status.ToString());
        }
        jobs.Cancel(id);
        logger.LogInformation("job {Id} cancellation requested", id);
        return NoContent();
    }
}
