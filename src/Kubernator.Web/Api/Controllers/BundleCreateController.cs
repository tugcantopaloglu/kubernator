using Kubernator.Web.Auth;
using Kubernator.Web.Jobs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kubernator.Web.Api.Controllers;

[ApiController]
[Route("api/v1/bundles")]
[Produces("application/json")]
[Tags("Bundles")]
[Authorize(Policy = ApiKeyScopes.GeneratePolicy)]
public sealed class BundleCreateController : ControllerBase
{
    private readonly IJobManager jobs;
    private readonly ILogger<BundleCreateController> logger;

    public BundleCreateController(IJobManager jobs, ILogger<BundleCreateController> logger)
    {
        this.jobs = jobs;
        this.logger = logger;
    }

    [HttpPost]
    [ProducesResponseType(typeof(JobAcceptedResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ApiProblem), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiProblem), StatusCodes.Status404NotFound)]
    public ActionResult<JobAcceptedResponse> Create([FromBody] BundleCreateRequest request)
    {
        var path = ApiPathHelpers.ResolveExistingPath(request?.Path, "path");
        ApiPathHelpers.RequireField(request!.OutputBundlePath, "outputBundlePath");
        var bundleOut = Path.GetFullPath(request.OutputBundlePath);
        var bundleDir = Path.GetDirectoryName(bundleOut);
        if (!string.IsNullOrEmpty(bundleDir))
        {
            Directory.CreateDirectory(bundleDir);
        }

        var keyId = User.FindFirst(ApiKeyScopes.KeyIdClaimType)?.Value;
        var keyName = User.FindFirst(ApiKeyScopes.KeyNameClaimType)?.Value;
        var captured = request with { Path = path, OutputBundlePath = bundleOut };

        var record = jobs.Enqueue("bundle", captured, keyId, keyName);
        logger.LogInformation("bundle job {Id} submitted by key={KeyId}", record.Id, keyId);
        return Accepted($"/api/v1/jobs/{record.Id}", new JobAcceptedResponse
        {
            Id = record.Id,
            Kind = record.Kind,
            Status = record.Status.ToString(),
            Location = $"/api/v1/jobs/{record.Id}"
        });
    }
}
