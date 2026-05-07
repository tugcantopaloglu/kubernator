using Kubernator.Core.Vault;
using Kubernator.Web.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kubernator.Web.Api.Controllers;

[ApiController]
[Route("api/v1/vault")]
[Produces("application/json")]
[Tags("Security")]
[Authorize(Policy = ApiKeyScopes.ReadPolicy)]
public sealed class VaultController : ControllerBase
{
    private readonly IKeyVault vault;
    private readonly ILogger<VaultController> logger;

    public VaultController(IKeyVault vault, ILogger<VaultController> logger)
    {
        this.vault = vault;
        this.logger = logger;
    }

    [HttpGet]
    [ProducesResponseType(typeof(VaultListResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<VaultListResponse>> List(CancellationToken ct)
    {
        logger.LogInformation("vault list requested");
        var entries = await vault.ListAsync(ct);
        return Ok(new VaultListResponse
        {
            Entries = entries.Select(VaultEntryDto.From).ToArray()
        });
    }

    [HttpGet("{id}")]
    [ProducesResponseType(typeof(VaultEntryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiProblem), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<VaultEntryDto>> Get(string id, CancellationToken ct)
    {
        var entry = await vault.GetAsync(id, ct);
        if (entry is null)
        {
            throw ApiException.NotFound("vault entry not found", id);
        }
        return Ok(VaultEntryDto.From(entry));
    }
}
