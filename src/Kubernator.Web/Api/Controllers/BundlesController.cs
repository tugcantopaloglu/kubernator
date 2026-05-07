using Kubernator.Core.Packaging;
using Microsoft.AspNetCore.Mvc;

namespace Kubernator.Web.Api.Controllers;

[ApiController]
[Route("api/v1/bundles")]
[Produces("application/json")]
[Tags("Bundles")]
public sealed class BundlesController : ControllerBase
{
    private readonly IBundleService bundles;
    private readonly ILogger<BundlesController> logger;

    public BundlesController(IBundleService bundles, ILogger<BundlesController> logger)
    {
        this.bundles = bundles;
        this.logger = logger;
    }

    [HttpPost("verify")]
    [ProducesResponseType(typeof(BundleVerifyResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiProblem), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiProblem), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BundleVerifyResponse>> Verify([FromBody] BundleVerifyRequest request, CancellationToken ct)
    {
        var bundlePath = ApiPathHelpers.ResolveExistingFile(request?.BundlePath, "bundlePath");
        logger.LogInformation("bundle verify {Path}", bundlePath);
        var result = await bundles.VerifyAsync(bundlePath, progress: null, ct);
        return Ok(new BundleVerifyResponse
        {
            Ok = result.Ok,
            Errors = result.Errors,
            Manifest = result.Manifest is null ? null : BundleManifestDto.From(result.Manifest)
        });
    }
}
