using Kubernator.Core.Audit;
using Microsoft.AspNetCore.Mvc;

namespace Kubernator.Web.Api.Controllers;

[ApiController]
[Route("api/v1/audit")]
[Produces("application/json")]
[Tags("Inspection")]
public sealed class AuditController : ControllerBase
{
    private readonly ManifestAuditor auditor;
    private readonly ILogger<AuditController> logger;

    public AuditController(ManifestAuditor auditor, ILogger<AuditController> logger)
    {
        this.auditor = auditor;
        this.logger = logger;
    }

    [HttpPost]
    [ProducesResponseType(typeof(AuditResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiProblem), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiProblem), StatusCodes.Status404NotFound)]
    public ActionResult<AuditResponse> Audit([FromBody] AuditRequest request)
    {
        var resolved = ApiPathHelpers.ResolveExistingDirectory(request?.Directory, "directory");
        logger.LogInformation("audit {Path} expectedNamespace={Namespace}", resolved, request!.ExpectedNamespace ?? "<any>");
        var result = auditor.AuditDirectory(resolved, request.ExpectedNamespace);
        return Ok(AuditResponse.From(result));
    }
}
