using Kubernator.Core.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace Kubernator.Web.Api.Controllers;

[ApiController]
[Route("api/v1/detect")]
[Produces("application/json")]
[Tags("Inspection")]
public sealed class DetectController : ControllerBase
{
    private readonly IDetectionService detection;
    private readonly ILogger<DetectController> logger;

    public DetectController(IDetectionService detection, ILogger<DetectController> logger)
    {
        this.detection = detection;
        this.logger = logger;
    }

    [HttpPost]
    [ProducesResponseType(typeof(DetectResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiProblem), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiProblem), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DetectResponse>> Detect([FromBody] DetectRequest request, CancellationToken ct)
    {
        var resolved = ApiPathHelpers.ResolveExistingPath(request?.Path, "path");
        logger.LogInformation("detect {Path}", resolved);
        var results = await detection.DetectAllAsync(resolved, ct);
        return Ok(new DetectResponse
        {
            Path = resolved,
            Results = results.Select(DetectionResultDto.From).ToArray()
        });
    }
}
