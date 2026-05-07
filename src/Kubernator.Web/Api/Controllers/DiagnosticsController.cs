using Kubernator.Core.Diagnostics;
using Kubernator.Web.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kubernator.Web.Api.Controllers;

[ApiController]
[Route("api/v1/diagnostics")]
[Produces("application/json")]
[Tags("System")]
[Authorize(Policy = ApiKeyScopes.ReadPolicy)]
public sealed class DiagnosticsController : ControllerBase
{
    private readonly IDiagnosticsService diagnostics;
    private readonly ILogger<DiagnosticsController> logger;

    public DiagnosticsController(IDiagnosticsService diagnostics, ILogger<DiagnosticsController> logger)
    {
        this.diagnostics = diagnostics;
        this.logger = logger;
    }

    [HttpGet]
    [ProducesResponseType(typeof(DiagnosticsResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<DiagnosticsResponse>> Get(CancellationToken ct)
    {
        logger.LogInformation("diagnostics requested");
        var report = await diagnostics.RunAsync(ct);
        return Ok(DiagnosticsResponse.From(report));
    }
}
