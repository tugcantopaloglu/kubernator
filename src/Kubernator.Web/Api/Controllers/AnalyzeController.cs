using Kubernator.Core.Abstractions;
using Kubernator.Web.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kubernator.Web.Api.Controllers;

[ApiController]
[Route("api/v1/analyze")]
[Produces("application/json")]
[Tags("Inspection")]
[Authorize(Policy = ApiKeyScopes.ReadPolicy)]
public sealed class AnalyzeController : ControllerBase
{
    private readonly IAnalysisService analysis;
    private readonly ILogger<AnalyzeController> logger;

    public AnalyzeController(IAnalysisService analysis, ILogger<AnalyzeController> logger)
    {
        this.analysis = analysis;
        this.logger = logger;
    }

    [HttpPost]
    [ProducesResponseType(typeof(AnalyzeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiProblem), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiProblem), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AnalyzeResponse>> Analyze([FromBody] AnalyzeRequest request, CancellationToken ct)
    {
        var resolved = ApiPathHelpers.ResolveExistingPath(request?.Path, "path");
        logger.LogInformation("analyze {Path}", resolved);
        var descriptor = await analysis.AnalyzeAsync(resolved, ct);
        return Ok(AnalyzeResponse.From(descriptor));
    }
}
