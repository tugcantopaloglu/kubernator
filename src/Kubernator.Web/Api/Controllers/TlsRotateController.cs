using Kubernator.Core.Tls.Rotation;
using Kubernator.Web.Auth;
using Kubernator.Web.Downloads;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kubernator.Web.Api.Controllers;

[ApiController]
[Route("api/v1/tls-rotate")]
[Produces("application/json")]
[Tags("Generation")]
[Authorize(Policy = ApiKeyScopes.GeneratePolicy)]
public sealed class TlsRotateController : ControllerBase
{
    private readonly ITlsRotationService rotation;
    private readonly ArtifactRegistry artifacts;
    private readonly ILogger<TlsRotateController> logger;

    public TlsRotateController(ITlsRotationService rotation, ArtifactRegistry artifacts, ILogger<TlsRotateController> logger)
    {
        this.rotation = rotation;
        this.artifacts = artifacts;
        this.logger = logger;
    }

    [HttpPost]
    [ProducesResponseType(typeof(TlsRotateResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiProblem), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<TlsRotateResponse>> Generate([FromBody] TlsRotateRequest request, CancellationToken ct)
    {
        ApiPathHelpers.RequireField(request?.SecretName, "secretName");
        ApiPathHelpers.RequireField(request!.Hostname, "hostname");
        var output = ApiPathHelpers.ResolveOutputDirectory(request.OutputDirectory, "tls-rotate");

        logger.LogInformation("tls-rotate secret={Secret} host={Host} ns={Namespace}",
            request.SecretName, request.Hostname, request.Namespace ?? "default");

        var options = new TlsRotationOptions
        {
            OutputDirectory = output,
            SecretName = request.SecretName,
            Hostname = request.Hostname,
            Namespace = request.Namespace ?? "default",
            Schedule = request.Schedule ?? "0 3 1 * *",
            DaysValid = request.DaysValid ?? 90,
            AdditionalHostnames = request.AdditionalHostnames ?? Array.Empty<string>(),
            ServiceAccountName = request.ServiceAccountName,
            CronJobName = request.CronJobName
        };
        var result = await rotation.GenerateAsync(options, ct);

        string? token = null;
        string? url = null;
        if (request.ReturnDownloadToken)
        {
            token = artifacts.RegisterDirectory(output, $"kubernator-tls-{request.SecretName}");
            url = $"/download/{token}";
        }

        return Ok(new TlsRotateResponse
        {
            OutputDirectory = result.OutputDirectory,
            WrittenFiles = result.WrittenFiles,
            ResolvedServiceAccountName = result.ResolvedServiceAccountName,
            ResolvedCronJobName = result.ResolvedCronJobName,
            DownloadToken = token,
            DownloadUrl = url
        });
    }
}
