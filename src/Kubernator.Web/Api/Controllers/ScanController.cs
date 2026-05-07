using Kubernator.Core.Abstractions;
using Kubernator.Core.Vulnerabilities;
using Kubernator.Web.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kubernator.Web.Api.Controllers;

[ApiController]
[Route("api/v1/scan")]
[Produces("application/json")]
[Tags("Security")]
[Authorize(Policy = ApiKeyScopes.ReadPolicy)]
public sealed class ScanController : ControllerBase
{
    private readonly IAnalysisService analysis;
    private readonly IVulnerabilityScanner scanner;
    private readonly ILogger<ScanController> logger;

    public ScanController(IAnalysisService analysis, IVulnerabilityScanner scanner, ILogger<ScanController> logger)
    {
        this.analysis = analysis;
        this.scanner = scanner;
        this.logger = logger;
    }

    [HttpPost]
    [ProducesResponseType(typeof(ScanResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiProblem), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiProblem), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ScanResponse>> Scan([FromBody] ScanRequest request, CancellationToken ct)
    {
        var path = ApiPathHelpers.ResolveExistingPath(request?.Path, "path");
        var descriptor = await analysis.AnalyzeAsync(path, ct);

        Severity minSeverity = Severity.Low;
        if (!string.IsNullOrWhiteSpace(request!.MinSeverity))
        {
            if (!Enum.TryParse<Severity>(request.MinSeverity, true, out minSeverity))
            {
                throw ApiException.BadRequest("invalid minSeverity", "expected: Low, Medium, High, Critical");
            }
        }

        var ignoreSet = (request.IgnoreIds is { Count: > 0 })
            ? new HashSet<string>(request.IgnoreIds, StringComparer.OrdinalIgnoreCase)
            : null;

        logger.LogInformation("scan {Path} ecosystem={Ecosystem} minSeverity={MinSeverity}",
            path, request.Ecosystem ?? "<auto>", minSeverity);

        var options = new ScanOptions
        {
            EcosystemOverride = string.IsNullOrWhiteSpace(request.Ecosystem) ? null : request.Ecosystem,
            IncludeUnknownVersions = request.IncludeUnknownVersions,
            IgnoreIds = ignoreSet,
            MinSeverity = minSeverity
        };
        var result = await scanner.ScanAsync(descriptor, options, ct);

        return Ok(new ScanResponse
        {
            Findings = result.Findings.Select(VulnerabilityFindingDto.From).ToArray(),
            PackagesScanned = result.PackagesScanned,
            Ecosystem = result.Ecosystem,
            DatabasePresent = result.DatabasePresent,
            DatabaseUpdatedAt = result.DatabaseUpdatedAt
        });
    }
}
