using Kubernator.Web.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kubernator.Web.Api.Controllers;

[ApiController]
[Route("api/v1/admin/api-keys")]
[Produces("application/json")]
[Tags("Admin")]
[Authorize(Policy = ApiKeyScopes.AdminPolicy)]
public sealed class AdminApiKeysController : ControllerBase
{
    private readonly IApiKeyStore store;
    private readonly ApiKeyRateLimitCache rateLimitCache;
    private readonly ILogger<AdminApiKeysController> logger;

    public AdminApiKeysController(IApiKeyStore store, ApiKeyRateLimitCache rateLimitCache, ILogger<AdminApiKeysController> logger)
    {
        this.store = store;
        this.rateLimitCache = rateLimitCache;
        this.logger = logger;
    }

    [HttpGet]
    [ProducesResponseType(typeof(ApiKeyListResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiKeyListResponse>> List(CancellationToken ct)
    {
        var records = await store.ListAsync(ct);
        return Ok(new ApiKeyListResponse
        {
            Keys = records.Select(ApiKeyDto.From).ToArray()
        });
    }

    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ApiKeyDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiProblem), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiKeyDto>> Get(string id, CancellationToken ct)
    {
        var record = await store.GetAsync(id, ct);
        if (record is null) throw ApiException.NotFound("api key not found", id);
        return Ok(ApiKeyDto.From(record));
    }

    [HttpPost]
    [ProducesResponseType(typeof(CreateApiKeyResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiProblem), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiProblem), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CreateApiKeyResponse>> Create([FromBody] CreateApiKeyRequest request, CancellationToken ct)
    {
        ApiPathHelpers.RequireField(request?.Name, "name");
        if (!ApiKeyScopes.TryParse(request!.Scope, out var scope))
        {
            throw ApiException.BadRequest("invalid scope", "expected: Read, Generate, Admin");
        }
        if (request.RateLimitPerMinute is { } rl && rl <= 0)
        {
            throw ApiException.BadRequest("invalid rateLimitPerMinute", "must be > 0");
        }
        if (request.ExpiresAt is { } exp && exp <= DateTimeOffset.UtcNow)
        {
            throw ApiException.BadRequest("invalid expiresAt", "must be in the future");
        }

        logger.LogInformation("admin creating api key name={Name} scope={Scope}", request.Name, scope);
        var result = await store.CreateAsync(new CreateApiKeyOptions
        {
            Name = request.Name,
            Scope = scope,
            ExpiresAt = request.ExpiresAt,
            RateLimitPerMinute = request.RateLimitPerMinute
        }, ct);
        rateLimitCache.Set(result.Record.Id, result.Record.RateLimitPerMinute);

        return CreatedAtAction(nameof(Get), new { id = result.Record.Id }, new CreateApiKeyResponse
        {
            Record = ApiKeyDto.From(result.Record),
            PlaintextKey = result.PlaintextKey
        });
    }

    [HttpPatch("{id}")]
    [ProducesResponseType(typeof(ApiKeyDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiProblem), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiKeyDto>> Disable(string id, [FromBody] DisableApiKeyRequest request, CancellationToken ct)
    {
        var existed = await store.DisableAsync(id, request.Disabled, ct);
        if (!existed) throw ApiException.NotFound("api key not found", id);
        var fresh = await store.GetAsync(id, ct);
        return Ok(ApiKeyDto.From(fresh!));
    }

    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiProblem), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        var ok = await store.RemoveAsync(id, ct);
        if (!ok) throw ApiException.NotFound("api key not found", id);
        rateLimitCache.Remove(id);
        logger.LogInformation("admin deleted api key {Id}", id);
        return NoContent();
    }
}
