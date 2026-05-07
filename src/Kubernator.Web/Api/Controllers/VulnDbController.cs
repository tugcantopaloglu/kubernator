using Kubernator.Core.Vulnerabilities;
using Kubernator.Web.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kubernator.Web.Api.Controllers;

[ApiController]
[Route("api/v1/vulndb")]
[Produces("application/json")]
[Tags("Security")]
[Authorize(Policy = ApiKeyScopes.ReadPolicy)]
public sealed class VulnDbController : ControllerBase
{
    private readonly IVulnerabilityDatabase database;
    private readonly ILogger<VulnDbController> logger;

    public VulnDbController(IVulnerabilityDatabase database, ILogger<VulnDbController> logger)
    {
        this.database = database;
        this.logger = logger;
    }

    [HttpGet]
    [ProducesResponseType(typeof(VulnDbStatusResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<VulnDbStatusResponse>> Status(CancellationToken ct)
    {
        logger.LogInformation("vulndb status requested");
        var manifest = await database.GetManifestAsync(ct);
        if (manifest is null)
        {
            return Ok(new VulnDbStatusResponse { Present = false, Ecosystems = Array.Empty<VulnDbEcosystemDto>() });
        }
        return Ok(new VulnDbStatusResponse
        {
            Present = true,
            SchemaVersion = manifest.SchemaVersion,
            UpdatedAt = manifest.UpdatedAt,
            Ecosystems = manifest.Ecosystems
                .Select(kv => new VulnDbEcosystemDto
                {
                    Name = kv.Key,
                    PackageCount = kv.Value.PackageCount,
                    VulnerabilityCount = kv.Value.VulnerabilityCount,
                    LastImportedAt = kv.Value.LastImportedAt
                })
                .ToArray()
        });
    }
}
