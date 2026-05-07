using Kubernator.Core.Updates;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kubernator.Web.Api.Controllers;

[ApiController]
[Route("api/v1/health")]
[AllowAnonymous]
[Produces("application/json")]
[Tags("System")]
public sealed class HealthController : ControllerBase
{
    private static readonly DateTimeOffset StartedAt = DateTimeOffset.UtcNow;

    [HttpGet]
    [ProducesResponseType(typeof(HealthResponse), StatusCodes.Status200OK)]
    public ActionResult<HealthResponse> Get()
    {
        var now = DateTimeOffset.UtcNow;
        return Ok(new HealthResponse
        {
            Status = "ok",
            Version = KubernatorVersion.Current,
            Timestamp = now,
            UptimeSeconds = (long)(now - StartedAt).TotalSeconds
        });
    }
}
